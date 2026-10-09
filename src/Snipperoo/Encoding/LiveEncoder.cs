namespace Snipperoo.Encoding;

/// <summary>
/// Encoder used while recording. Quality is near-lossless because the clip is re-encoded to size afterwards;
/// the goal here is low CPU load at full frame rate.
/// </summary>
internal sealed record LiveEncoder(string Name, string[] Args)
{
    private static readonly LiveEncoder[] HardwareCandidates =
    [
        new("h264_amf", ["-c:v", "h264_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "16", "-qp_p", "18"]),
        new("h264_nvenc", ["-c:v", "h264_nvenc", "-preset", "p2", "-tune", "ll", "-rc", "constqp", "-qp", "17"]),
        new("h264_qsv", ["-c:v", "h264_qsv", "-preset", "veryfast", "-global_quality", "18"]),
    ];

    public static readonly LiveEncoder Software =
        new("libx264", ["-c:v", "libx264", "-preset", "ultrafast", "-crf", "16"]);

    /// <summary>
    /// Returns the first hardware encoder that can open a session on this machine, else libx264.
    /// Encoders can be compiled into ffmpeg but have no matching GPU, so each one is tried for real.
    /// </summary>
    public static async Task<LiveEncoder> DetectAsync(Ffmpeg ffmpeg)
    {
        foreach (var candidate in HardwareCandidates)
        {
            try
            {
                await ffmpeg.RunAsync(
                [
                    "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "color=black:s=256x256:r=30", "-frames:v", "3",
                    "-pix_fmt", "nv12", .. candidate.Args, "-f", "null", "-",
                ]);
                Log.Info($"Live encoder: {candidate.Name}");
                return candidate;
            }
            catch (FfmpegException)
            {
                // Not usable here; try the next one.
            }
        }

        Log.Info($"Live encoder: {Software.Name} (no hardware encoder available)");
        return Software;
    }
}
