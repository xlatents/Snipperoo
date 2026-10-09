using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Snipperoo.Encoding;

namespace Snipperoo.Setup;

/// <summary>
/// Gets ffmpeg onto the machine when it is missing: winget first (Gyan.FFmpeg, per user, no admin), then a direct
/// download of the same build from its GitHub releases into <see cref="DownloadFolder"/>.
/// </summary>
internal static class FfmpegInstaller
{
    private const string WingetPackageId = "Gyan.FFmpeg";
    // The project winget's Gyan.FFmpeg package downloads from. "essentials" has everything Snipperoo uses
    // (ddagrab, AMF/NVENC/QSV, libx264) at less than half the size of "full".
    private const string ReleaseApi = "https://api.github.com/repos/GyanD/codexffmpeg/releases/latest";
    private const string AssetSuffix = "essentials_build.zip";

    private static readonly TimeSpan WingetTimeout = TimeSpan.FromMinutes(5);
    private static Task<Ffmpeg>? _running;

    /// <summary>Snipperoo's own copy of ffmpeg; inside the install folder so uninstalling removes it.</summary>
    public static string DownloadFolder => Path.Combine(Installer.InstallFolder, "ffmpeg");

    /// <summary>
    /// Returns ffmpeg, installing it first if needed. Concurrent callers share one attempt. Reports short status
    /// lines to <paramref name="progress"/>. Throws with a user-readable message on failure.
    /// </summary>
    public static Task<Ffmpeg> EnsureAsync(string configuredPath, IProgress<string>? progress = null)
    {
        if (Ffmpeg.Locate(configuredPath) is { } existing)
            return Task.FromResult(existing);
        if (_running is { IsCompleted: false })
            return _running;
        return _running = InstallAsync(configuredPath, progress);
    }

    private static async Task<Ffmpeg> InstallAsync(string configuredPath, IProgress<string>? progress)
    {
        progress?.Report("Installing ffmpeg with winget…");
        await TryWingetAsync();
        if (Ffmpeg.Locate(configuredPath) is { } fromWinget)
            return fromWinget;

        Log.Info("ffmpeg not found after winget, downloading it directly");
        try
        {
            await DownloadAsync(DownloadFolder, progress);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("ffmpeg download failed", ex);
            throw new InvalidOperationException("Couldn't download ffmpeg. Check your internet connection and try again.", ex);
        }

        return Ffmpeg.Locate(configuredPath)
            ?? throw new InvalidOperationException("ffmpeg was downloaded but could not be found. Details are in the log.");
    }

    // Best effort: winget may be missing, outdated, blocked by policy, or install somewhere Locate does not look.
    private static async Task TryWingetAsync()
    {
        var psi = new ProcessStartInfo("winget")
        {
            ArgumentList =
            {
                "install", "--id", WingetPackageId, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements",
            },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(WingetTimeout);
            await process.WaitForExitAsync(timeout.Token);
            Log.Info($"winget exited with 0x{process.ExitCode:X8}: {(await output).Trim()} {(await errors).Trim()}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log.Info("winget is not available");
        }
        catch (OperationCanceledException)
        {
            Log.Error("winget did not finish in time");
        }
    }

    /// <summary>
    /// Downloads the latest essentials build into <paramref name="folder"/> (ffmpeg.exe and ffprobe.exe only),
    /// checking the SHA-256 digest GitHub publishes for the file.
    /// </summary>
    internal static async Task DownloadAsync(string folder, IProgress<string>? progress)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Snipperoo/{typeof(FfmpegInstaller).Assembly.GetName().Version}");

        var release = await http.GetFromJsonAsync<JsonElement>(ReleaseApi);
        var asset = release.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("name").GetString()!.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase));
        string url = asset.GetProperty("browser_download_url").GetString()!;
        string? digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
        Log.Info($"Downloading {url}");

        string zipPath = Path.Combine(Path.GetTempPath(), $"snipperoo-ffmpeg-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(http, url, zipPath, progress);
            VerifyDigest(zipPath, digest);
            progress?.Report("Unpacking…");
            Extract(zipPath, folder);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string path, IProgress<string>? progress)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(path);
        var buffer = new byte[1 << 16];
        long done = 0;
        int lastPercent = -1;
        for (int read; (read = await source.ReadAsync(buffer)) > 0;)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            int percent = total > 0 ? (int)(done * 100 / total.Value) : -1;
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress?.Report(percent >= 0 ? $"Downloading ffmpeg… {percent}%" : $"Downloading ffmpeg… {done / 1_000_000} MB");
            }
        }
    }

    private static void VerifyDigest(string path, string? digest)
    {
        // GitHub's digest looks like "sha256:<hex>". Older releases have none; HTTPS from GitHub is then all we have.
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return;
        using var file = File.OpenRead(path);
        string actual = Convert.ToHexStringLower(SHA256.HashData(file));
        if (!string.Equals(actual, digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The ffmpeg download was corrupted. Please try again.");
    }

    private static void Extract(string zipPath, string folder)
    {
        Directory.CreateDirectory(folder);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (string name in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            var entry = zip.Entries.First(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                               && e.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
            // Extract next to the target and swap, so a half-written file is never picked up by Locate.
            string target = Path.Combine(folder, name);
            string partial = target + ".partial";
            entry.ExtractToFile(partial, overwrite: true);
            File.Move(partial, target, overwrite: true);
        }
    }
}
