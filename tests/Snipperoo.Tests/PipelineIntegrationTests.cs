using System.Drawing;
using Snipperoo.Capture;
using Snipperoo.Encoding;
using Xunit.Abstractions;

namespace Snipperoo.Tests;

/// <summary>
/// Records real screen regions and encodes them. Needs ffmpeg and a desktop session;
/// run with: dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public class PipelineIntegrationTests(ITestOutputHelper output)
{
    public static TheoryData<int> MonitorIndices()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < MonitorOutput.Enumerate().Count; i++)
            data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(MonitorIndices))]
    public async Task Records_and_encodes_a_region_under_the_limit(int monitorIndex)
    {
        var ffmpeg = Ffmpeg.Locate("") ?? throw new InvalidOperationException("ffmpeg not found");
        var monitor = MonitorOutput.Enumerate()[monitorIndex];
        output.WriteLine($"{monitor}");

        var b = monitor.DesktopBounds;
        var region = CaptureRegion.Create(new Rectangle(b.X + 100, b.Y + 50, 641, 481), monitor)!;
        var settings = new AppSettings { OutputFolder = Path.Combine(Path.GetTempPath(), "SnipperooTests") };
        const long maxBytes = 2_000_000;

        using var recorder = Recorder.Start(region, settings, ffmpeg, await LiveEncoder.DetectAsync(ffmpeg));
        await Task.Delay(TimeSpan.FromSeconds(4));
        var recording = await recorder.StopAsync();
        output.WriteLine($"audio leads video by {recording.AudioLead.TotalMilliseconds:0} ms");
        Assert.InRange(recording.AudioLead.TotalMilliseconds, -50, 1000);

        var clip = await new ClipEncoder(ffmpeg).EncodeAsync(recording, settings.OutputFolder, maxBytes);
        output.WriteLine($"{clip.Path}: {clip.Bytes} bytes, {clip.Plan}");
        Directory.Delete(recording.WorkFolder, recursive: true);
        File.Delete(clip.Path);

        Assert.InRange(clip.Bytes, 1, maxBytes);
        Assert.InRange(clip.Duration.TotalSeconds, 3, 5);
        Assert.Equal((640, 480), (clip.Plan.Width, clip.Plan.Height));
    }
}
