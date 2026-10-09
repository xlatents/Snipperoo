using System.Diagnostics;
using System.Globalization;

namespace Snipperoo.Encoding;

/// <summary>Locates ffmpeg/ffprobe and runs them as hidden child processes.</summary>
internal sealed class Ffmpeg
{
    public string FfmpegPath { get; }
    public string FfprobePath { get; }

    private Ffmpeg(string ffmpegPath, string ffprobePath)
    {
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
    }

    private static readonly string WingetFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet");

    /// <summary>
    /// Finds ffmpeg with ffprobe next to it, or returns null. Looks at <paramref name="configuredPath"/>, next to the
    /// exe, Snipperoo's own download, PATH, then winget's folders.
    /// </summary>
    public static Ffmpeg? Locate(string configuredPath)
    {
        IEnumerable<string> candidates = CandidateFolders().Select(dir => Path.Combine(dir, "ffmpeg.exe"));
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates = candidates.Prepend(Environment.ExpandEnvironmentVariables(configuredPath));

        foreach (string ffmpeg in candidates)
        {
            string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                return new Ffmpeg(ffmpeg, ffprobe);
        }
        return null;
    }

    private static IEnumerable<string> CandidateFolders()
    {
        yield return AppContext.BaseDirectory;
        yield return Setup.FfmpegInstaller.DownloadFolder;

        // PATH as this process got it, plus the current registry values: installers (winget included) update PATH
        // after we started, and running processes never see that.
        foreach (string? path in new[]
                 {
                     Environment.GetEnvironmentVariable("PATH"),
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
                 })
        {
            foreach (string dir in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return Environment.ExpandEnvironmentVariables(dir);
        }

        // winget links portable packages into Links only when it may create symlinks (Developer Mode or admin);
        // otherwise the files stay in the package folder.
        yield return Path.Combine(WingetFolder, "Links");
        foreach (string bin in WingetPackageBins(Path.Combine(WingetFolder, "Packages")))
            yield return bin;
    }

    /// <summary>The bin folders of Gyan.FFmpeg packages under winget's Packages folder.</summary>
    internal static IEnumerable<string> WingetPackageBins(string packagesFolder)
    {
        if (!Directory.Exists(packagesFolder))
            return [];
        return Directory.EnumerateDirectories(packagesFolder, "Gyan.FFmpeg*")
            .SelectMany(package => Directory.EnumerateDirectories(package, "ffmpeg-*"))
            .Select(build => Path.Combine(build, "bin"))
            .Where(Directory.Exists);
    }

    /// <summary>
    /// Starts ffmpeg with stdin open (for the 'q' stop command) and stderr collected.
    /// <paramref name="onStderrLine"/> sees every stderr line, on a background thread.
    /// </summary>
    public FfmpegProcess Start(IEnumerable<string> args, Action<string>? onStderrLine = null) =>
        new(FfmpegPath, args, onStderrLine);

    /// <summary>Runs ffmpeg to completion. Throws FfmpegException with the error output on a non-zero exit.</summary>
    public async Task RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        using var process = Start(args);
        await process.WaitForExitAsync(ct);
        process.ThrowIfFailed();
    }

    /// <summary>Writes a small JPEG of the frame at <paramref name="atSeconds"/> to <paramref name="output"/>.</summary>
    public Task ExtractThumbnailAsync(string video, string output, double atSeconds, int width) => RunAsync(
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-ss", atSeconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", video,
        "-frames:v", "1", "-vf", $"scale={width}:-2", "-q:v", "3", output,
    ]);

    /// <summary>Container duration in seconds, read with ffprobe.</summary>
    public async Task<double> GetDurationAsync(string file, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(FfprobePath)
        {
            ArgumentList = { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file },
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            throw new FfmpegException($"ffprobe could not read the duration of {Path.GetFileName(file)}.");
        return seconds;
    }
}

/// <summary>A running ffmpeg process. Keeps the tail of stderr for error reports.</summary>
internal sealed class FfmpegProcess : IDisposable
{
    private const int TailLines = 20;

    private readonly Process _process;
    private readonly Queue<string> _stderrTail = new();

    public FfmpegProcess(string exe, IEnumerable<string> args, Action<string>? onStderrLine = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        Log.Info("ffmpeg " + string.Join(' ', psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        _process = new Process { StartInfo = psi };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            onStderrLine?.Invoke(e.Data);
            lock (_stderrTail)
            {
                _stderrTail.Enqueue(e.Data);
                if (_stderrTail.Count > TailLines)
                    _stderrTail.Dequeue();
            }
        };
        _process.Start();
        _process.BeginErrorReadLine();
    }

    public bool HasExited => _process.HasExited;

    public Task WaitForExitAsync(CancellationToken ct = default) => _process.WaitForExitAsync(ct);

    /// <summary>Asks ffmpeg to finish the file and exit; kills it if it does not within <paramref name="timeout"/>.</summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (_process.HasExited)
            return;
        try
        {
            await _process.StandardInput.WriteAsync('q');
            await _process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // Process is already exiting.
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Error("ffmpeg did not stop in time, killing it");
            _process.Kill();
            await _process.WaitForExitAsync();
        }
    }

    public void ThrowIfFailed()
    {
        if (_process.ExitCode != 0)
            throw new FfmpegException($"ffmpeg exited with code {_process.ExitCode}:{Environment.NewLine}{ErrorOutput}");
    }

    public string ErrorOutput
    {
        get
        {
            lock (_stderrTail)
                return string.Join(Environment.NewLine, _stderrTail);
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill();
        _process.Dispose();
    }
}

internal sealed class FfmpegException(string message) : Exception(message);
