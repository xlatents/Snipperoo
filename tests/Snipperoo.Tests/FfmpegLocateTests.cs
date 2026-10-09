using Snipperoo.Encoding;
using Snipperoo.Setup;

namespace Snipperoo.Tests;

public class FfmpegLocateTests
{
    [Fact]
    public void Finds_bin_folders_in_winget_package_layout()
    {
        string root = Directory.CreateTempSubdirectory("snipperoo-winget").FullName;
        try
        {
            string bin = Path.Combine(root, "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-9.0.2-full_build", "bin");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(Path.Combine(root, "Other.Package_x", "ffmpeg-1.0", "bin"));

            Assert.Equal([bin], Ffmpeg.WingetPackageBins(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_packages_folder_yields_nothing()
    {
        Assert.Empty(Ffmpeg.WingetPackageBins(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    /// <summary>Downloads ~110 MB. Run with: dotnet test --filter Category=Network</summary>
    [Fact]
    [Trait("Category", "Network")]
    public async Task Direct_download_yields_working_ffmpeg_with_capture_and_encoders()
    {
        string folder = Directory.CreateTempSubdirectory("snipperoo-ffmpeg").FullName;
        try
        {
            var statuses = new List<string>();
            await FfmpegInstaller.DownloadAsync(folder, new SyncProgress(statuses.Add));

            Assert.True(File.Exists(Path.Combine(folder, "ffmpeg.exe")));
            Assert.True(File.Exists(Path.Combine(folder, "ffprobe.exe")));
            Assert.Contains(statuses, s => s.EndsWith("100%"));

            var ffmpeg = Ffmpeg.Locate(Path.Combine(folder, "ffmpeg.exe"))!;
            Assert.Equal(Path.Combine(folder, "ffmpeg.exe"), ffmpeg.FfmpegPath);
            await ffmpeg.RunAsync(["-hide_banner", "-f", "lavfi", "-i", "color=black:s=64x64", "-frames:v", "1", "-c:v", "libx264", "-f", "null", "-"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // Progress<T> posts to the thread pool; this reports inline so the list is complete when the download returns.
    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
