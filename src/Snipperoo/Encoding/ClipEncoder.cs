using System.Globalization;
using Snipperoo.Capture;

namespace Snipperoo.Encoding;

/// <summary>A finished clip.</summary>
internal sealed record Clip(string Path, long Bytes, TimeSpan Duration, EncodePlan Plan);

/// <summary>Turns a <see cref="Recording"/> into an MP4 under the size limit (H.264 + AAC, fast-start).</summary>
internal sealed class ClipEncoder(Ffmpeg ffmpeg)
{
    private const int MaxAttempts = 3;

    // Aim a little under the limit on a retry so one more pass is enough.
    private const double RetryMargin = 0.95;

    public async Task<Clip> EncodeAsync(Recording recording, string outputFolder, long maxBytes)
    {
        Directory.CreateDirectory(outputFolder);
        string output = Path.Combine(outputFolder, $"Snipperoo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");

        double duration = await ffmpeg.GetDurationAsync(recording.VideoPath);
        var plan = EncodePlanner.Plan(
            recording.Region.Bounds.Size, recording.FrameRate, duration, recording.AudioPath is not null, maxBytes);

        for (int attempt = 1; ; attempt++)
        {
            Log.Info($"Encode attempt {attempt}: {plan}");
            await ffmpeg.RunAsync(BuildArgs(recording, duration, plan, output));

            long size = new FileInfo(output).Length;
            if (size <= maxBytes)
                return new Clip(output, size, TimeSpan.FromSeconds(duration), plan);
            if (attempt == MaxAttempts)
                throw new FfmpegException($"Clip is {size / 1e6:0.0} MB after {MaxAttempts} attempts, over the limit.");

            plan = plan.WithVideoRateScaled((double)maxBytes / size * RetryMargin);
        }
    }

    private static List<string> BuildArgs(Recording recording, double videoDuration, EncodePlan plan, string output)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-y", "-i", recording.VideoPath };

        if (recording.AudioPath is not null)
        {
            // Audio usually starts first: skip its head. If it started later, delay it instead.
            double lead = recording.AudioLead.TotalSeconds;
            args.AddRange(lead >= 0 ? ["-ss", Seconds(lead)] : ["-itsoffset", Seconds(-lead)]);
            args.AddRange(["-i", recording.AudioPath]);
        }

        var filters = new List<string>();
        if (recording.Region.UprightFilter is { } upright)
            filters.Add(upright);
        if (plan.FrameRate != recording.FrameRate)
            filters.Add($"fps={plan.FrameRate}");
        if (plan.Width != recording.Region.Bounds.Width || plan.Height != recording.Region.Bounds.Height)
            filters.Add($"scale={plan.Width}:{plan.Height}:flags=lanczos");
        filters.Add("format=yuv420p");

        args.AddRange(
        [
            "-map", "0:v:0",
            "-vf", string.Join(',', filters),
            "-c:v", "libx264", "-preset", plan.Preset, "-crf", plan.Crf.ToString(CultureInfo.InvariantCulture),
            "-maxrate", Rate(plan.VideoMaxRate), "-bufsize", Rate(plan.VideoBufferSize),
            "-profile:v", "high",
            "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
        ]);

        if (recording.AudioPath is not null)
            args.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", Rate(plan.AudioBitrate), "-ac", "2", "-ar", "48000"]);

        args.AddRange(["-t", Seconds(videoDuration), "-movflags", "+faststart", output]);
        return args;
    }

    private static string Seconds(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Rate(long bitsPerSecond) => bitsPerSecond.ToString(CultureInfo.InvariantCulture);
}
