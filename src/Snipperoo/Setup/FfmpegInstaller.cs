using System.Diagnostics;
using Snipperoo.Encoding;

namespace Snipperoo.Setup;

/// <summary>Gets ffmpeg onto the machine with winget (a portable package: per-user, no admin) when it is missing.</summary>
internal static class FfmpegInstaller
{
    private const string PackageId = "Gyan.FFmpeg";

    /// <summary>Returns ffmpeg, installing it first if needed. Throws with a user-readable message on failure.</summary>
    public static async Task<Ffmpeg> EnsureAsync(string configuredPath)
    {
        if (Ffmpeg.Locate(configuredPath) is { } existing)
            return existing;

        Log.Info("ffmpeg missing, installing with winget");
        var psi = new ProcessStartInfo("winget")
        {
            ArgumentList =
            {
                "install", "--id", PackageId, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("winget is not available. Install ffmpeg manually, then restart Snipperoo.");
        }

        using (process)
        {
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            Log.Info($"winget exited with {process.ExitCode}: {output.Trim()}");
        }

        return Ffmpeg.Locate(configuredPath)
            ?? throw new InvalidOperationException("ffmpeg could not be installed. Check your internet connection and try again.");
    }
}
