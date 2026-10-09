using System.Drawing;

namespace Snipperoo.Encoding;

/// <summary>Final x264/AAC settings for one clip. Rates are in bits per second.</summary>
internal sealed record EncodePlan(
    int Width,
    int Height,
    int FrameRate,
    long VideoMaxRate,
    long VideoBufferSize,
    int AudioBitrate,
    string Preset,
    int Crf)
{
    /// <summary>Same plan with the video rate scaled by <paramref name="factor"/>; used when a clip comes out too big.</summary>
    public EncodePlan WithVideoRateScaled(double factor) => this with
    {
        VideoMaxRate = (long)(VideoMaxRate * factor),
        VideoBufferSize = (long)(VideoBufferSize * factor),
    };
}

/// <summary>
/// Picks the best quality settings that keep a clip under a byte limit (see docs/DESIGN.md, "Size targeting").
/// x264 runs at CRF quality capped by a VBV max rate, and VBV bounds the total size to
/// maxrate * duration + bufsize, which is what makes the limit hold.
/// </summary>
internal static class EncodePlanner
{
    private const double ContainerOverhead = 0.97;
    private const double BufferSeconds = 1.0;
    private const int Crf = 18;

    private const int AudioBitrateMax = 128_000;
    private const int AudioBitrateMin = 32_000;
    private const double AudioMaxShare = 0.15;

    // Bits per pixel per frame below which 60 fps or the full resolution would look blocky in H.264.
    private const double MinBppForHighFrameRate = 0.06;
    private const double MinBppForResolution = 0.04;
    private const int ReducedFrameRate = 30;
    private static readonly int[] ShortSideSteps = [1080, 900, 720, 540, 480, 360];

    /// <param name="source">Upright size of the recording.</param>
    /// <param name="sourceFrameRate">Frame rate it was captured at.</param>
    /// <param name="durationSeconds">Clip length.</param>
    /// <param name="hasAudio">Whether an audio track will be encoded.</param>
    /// <param name="maxBytes">Hard limit for the output file.</param>
    /// <exception cref="ArgumentOutOfRangeException">Even minimum-rate audio would not fit.</exception>
    public static EncodePlan Plan(Size source, int sourceFrameRate, double durationSeconds, bool hasAudio, long maxBytes)
    {
        double duration = Math.Max(durationSeconds, 0.5);
        double budgetBits = maxBytes * 8.0 * ContainerOverhead;

        int audioBitrate = hasAudio
            ? (int)Math.Clamp(budgetBits * AudioMaxShare / duration, AudioBitrateMin, AudioBitrateMax)
            : 0;
        double videoBits = budgetBits - (double)audioBitrate * duration;
        if (videoBits <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "The clip is too long to fit in the size limit.");
        long maxRate = (long)(videoBits / (duration + BufferSeconds));

        int frameRate = sourceFrameRate;
        if (frameRate > ReducedFrameRate && BitsPerPixel(maxRate, source, frameRate) < MinBppForHighFrameRate)
            frameRate = ReducedFrameRate;

        Size size = source;
        foreach (int step in ShortSideSteps)
        {
            if (BitsPerPixel(maxRate, size, frameRate) >= MinBppForResolution)
                break;
            if (step < ShortSide(size))
                size = ScaleToShortSide(source, step);
        }

        double pixelsToEncode = (double)size.Width * size.Height * frameRate * duration;
        return new EncodePlan(
            size.Width,
            size.Height,
            frameRate,
            maxRate,
            (long)(maxRate * BufferSeconds),
            audioBitrate,
            PresetFor(pixelsToEncode),
            Crf);
    }

    // Keeps encode time to a few seconds on a modern desktop CPU, trading x264 efficiency for speed on long clips.
    private static string PresetFor(double pixels) => pixels switch
    {
        <= 6e8 => "slow",
        <= 2.5e9 => "medium",
        <= 8e9 => "faster",
        _ => "veryfast",
    };

    private static double BitsPerPixel(long bitrate, Size size, int frameRate) =>
        bitrate / ((double)size.Width * size.Height * frameRate);

    private static int ShortSide(Size size) => Math.Min(size.Width, size.Height);

    private static Size ScaleToShortSide(Size source, int shortSide)
    {
        double factor = (double)shortSide / ShortSide(source);
        return new Size(Even(source.Width * factor), Even(source.Height * factor));
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}
