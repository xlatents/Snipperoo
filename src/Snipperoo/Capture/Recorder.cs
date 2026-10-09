using System.Globalization;
using System.Text.RegularExpressions;
using Snipperoo.Encoding;

namespace Snipperoo.Capture;

/// <summary>
/// Raw output of one recording, ready for the final encode. <paramref name="AudioPath"/> is null without audio;
/// <paramref name="AudioLead"/> is how much earlier the audio file starts than the video.
/// </summary>
internal sealed record Recording(
    CaptureRegion Region,
    int FrameRate,
    string WorkFolder,
    string VideoPath,
    string? AudioPath,
    TimeSpan AudioLead);

/// <summary>
/// Records a <see cref="CaptureRegion"/>: ffmpeg ddagrab plus a hardware encoder for video, NAudio for system audio.
/// Sync: ffmpeg prints the wall-clock time of the first frame, compared with the audio start (see docs/DESIGN.md).
/// </summary>
internal sealed partial class Recorder : IDisposable
{
    // Written by ffmpeg's expression print() for frame 0, e.g. "[Eval @ 000000e137ffef30] 1791574475832953.000000".
    [GeneratedRegex(@"^\[Eval @ \w+\] (\d+)")]
    private static partial Regex FirstFrameTimeLine();

    private readonly FfmpegProcess _video;
    private readonly AudioRecorder? _audio;
    private readonly string _workFolder;
    private readonly string _videoPath;
    private readonly string? _audioPath;
    private readonly CaptureRegion _region;
    private readonly int _frameRate;
    private long _firstFrameUnixMicros;

    private Recorder(CaptureRegion region, int frameRate, string workFolder, string videoPath, FfmpegProcess video,
        AudioRecorder? audio, string? audioPath)
    {
        _region = region;
        _frameRate = frameRate;
        _workFolder = workFolder;
        _videoPath = videoPath;
        _video = video;
        _audio = audio;
        _audioPath = audioPath;
    }

    /// <summary>Completes when ffmpeg exits, which before <see cref="StopAsync"/> means it failed.</summary>
    public Task Exited => _video.WaitForExitAsync();

    public string ErrorOutput => _video.ErrorOutput;

    public static Recorder Start(CaptureRegion region, AppSettings settings, Ffmpeg ffmpeg, LiveEncoder encoder)
    {
        string workFolder = Path.Combine(Path.GetTempPath(), "Snipperoo", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(workFolder);

        AudioRecorder? audio = null;
        string? audioPath = null;
        if (settings.RecordSystemAudio)
        {
            try
            {
                audioPath = Path.Combine(workFolder, "audio.wav");
                audio = new AudioRecorder(audioPath);
                audio.Start();
            }
            catch (Exception ex)
            {
                // No output device or it is busy; a silent clip beats no clip.
                Log.Error("System audio unavailable, recording without it", ex);
                audio?.Dispose();
                audio = null;
                audioPath = null;
            }
        }

        string videoPath = Path.Combine(workFolder, "video.mkv");
        Recorder? recorder = null;
        var video = ffmpeg.Start(BuildVideoArgs(region, settings, encoder, videoPath), line => recorder?.OnStderrLine(line));
        recorder = new Recorder(region, settings.FrameRate, workFolder, videoPath, video, audio, audioPath);
        return recorder;
    }

    /// <summary>Stops video and audio at the same moment and returns the files.</summary>
    public async Task<Recording> StopAsync()
    {
        var videoStop = _video.StopAsync(TimeSpan.FromSeconds(10));
        var audioStop = _audio?.StopAsync() ?? Task.CompletedTask;
        await Task.WhenAll(videoStop, audioStop);
        _video.ThrowIfFailed();

        return new Recording(_region, _frameRate, _workFolder, _videoPath, _audioPath, AudioLead());
    }

    private TimeSpan AudioLead()
    {
        if (_audio is null)
            return TimeSpan.Zero;

        long micros = Interlocked.Read(ref _firstFrameUnixMicros);
        if (micros == 0)
        {
            Log.Error("ffmpeg did not report the first frame time; audio may be out of sync");
            return TimeSpan.Zero;
        }

        var lead = DateTime.UnixEpoch.AddTicks(micros * 10) - _audio.StartedAtUtc;
        Log.Info($"Audio leads video by {lead.TotalMilliseconds:0} ms");
        return lead;
    }

    private void OnStderrLine(string line)
    {
        if (Interlocked.Read(ref _firstFrameUnixMicros) == 0 && FirstFrameTimeLine().Match(line) is { Success: true } match)
            Interlocked.Exchange(ref _firstFrameUnixMicros, long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private static List<string> BuildVideoArgs(CaptureRegion region, AppSettings settings, LiveEncoder encoder, string output)
    {
        var crop = region.FramebufferCrop;
        string grab = string.Create(CultureInfo.InvariantCulture,
            $"ddagrab=output_idx={region.Monitor.OutputIndex}:framerate={settings.FrameRate}" +
            $":draw_mouse={(settings.ShowCursor ? 1 : 0)}" +
            $":offset_x={crop.X}:offset_y={crop.Y}:video_size={crop.Width}x{crop.Height}");

        return
        [
            "-hide_banner", "-loglevel", "error", "-nostats", "-y",
            // ddagrab only sees outputs of the adapter its D3D11 device was created on.
            "-init_hw_device", $"d3d11va=dd:{region.Monitor.AdapterIndex}", "-filter_hw_device", "dd",
            // Explicit BT.709 conversion on the CPU: the hardware encoders tag RGB input inconsistently.
            // setpts leaves timestamps alone; it only prints the wall-clock time (µs) as frame 0 passes, at error level.
            "-filter_complex", grab + @",setpts=PTS+if(eq(N\,0)\,0*print(RTCTIME\,16)\,0)," +
                "hwdownload,format=bgra,scale=out_color_matrix=bt709:out_range=tv,format=nv12",
            .. encoder.Args,
            "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
            output,
        ];
    }

    public void Dispose()
    {
        _video.Dispose();
        _audio?.Dispose();
    }
}
