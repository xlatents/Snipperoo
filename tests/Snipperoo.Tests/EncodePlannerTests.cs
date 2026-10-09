using System.Drawing;
using Snipperoo.Encoding;

namespace Snipperoo.Tests;

public class EncodePlannerTests
{
    private const long TwentyMB = 20_000_000;

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(60, true)]
    [InlineData(300, true)]
    [InlineData(1800, true)]
    [InlineData(45, false)]
    public void Worst_case_size_stays_under_limit(double seconds, bool audio)
    {
        var plan = EncodePlanner.Plan(new Size(2560, 1440), 60, seconds, audio, TwentyMB);

        // VBV bound: video can never exceed maxrate * duration + bufsize.
        double worstBytes = (plan.VideoMaxRate * seconds + plan.VideoBufferSize + plan.AudioBitrate * seconds) / 8;
        Assert.True(worstBytes < TwentyMB, $"worst case {worstBytes:N0} bytes");
    }

    [Fact]
    public void Short_clip_keeps_full_quality()
    {
        var plan = EncodePlanner.Plan(new Size(2560, 1440), 60, 10, true, TwentyMB);

        Assert.Equal((2560, 1440, 60), (plan.Width, plan.Height, plan.FrameRate));
        Assert.Equal(128_000, plan.AudioBitrate);
    }

    [Fact]
    public void Minute_long_clip_drops_frame_rate_before_resolution()
    {
        var plan = EncodePlanner.Plan(new Size(2560, 1440), 60, 60, true, TwentyMB);

        Assert.Equal(30, plan.FrameRate);
        Assert.Equal((1600, 900), (plan.Width, plan.Height));
    }

    [Fact]
    public void Long_clip_scales_down_and_trims_audio()
    {
        var plan = EncodePlanner.Plan(new Size(1920, 1080), 60, 600, true, TwentyMB);

        Assert.Equal(30, plan.FrameRate);
        Assert.Equal(360, Math.Min(plan.Width, plan.Height));
        Assert.InRange(plan.AudioBitrate, 32_000, 127_999);
    }

    [Fact]
    public void Portrait_scales_by_short_side_and_keeps_aspect()
    {
        var plan = EncodePlanner.Plan(new Size(1080, 1920), 60, 120, false, TwentyMB);

        Assert.True(plan.Width < plan.Height);
        Assert.Equal(0, plan.Width % 2);
        Assert.Equal(0, plan.Height % 2);
        Assert.Equal(1080.0 / 1920, (double)plan.Width / plan.Height, 2);
    }

    [Fact]
    public void Small_region_is_never_upscaled()
    {
        var plan = EncodePlanner.Plan(new Size(400, 300), 60, 900, false, TwentyMB);

        Assert.True(plan.Width <= 400 && plan.Height <= 300);
    }

    [Fact]
    public void Preset_gets_faster_as_work_grows()
    {
        string Preset(double seconds) => EncodePlanner.Plan(new Size(1920, 1080), 60, seconds, true, 500_000_000).Preset;

        Assert.Equal("slow", Preset(3));
        Assert.Equal("veryfast", Preset(600));
    }

    [Fact]
    public void Retry_scales_video_rate_only()
    {
        var plan = EncodePlanner.Plan(new Size(1920, 1080), 60, 30, true, TwentyMB);
        var smaller = plan.WithVideoRateScaled(0.5);

        Assert.InRange(smaller.VideoMaxRate, plan.VideoMaxRate / 2 - 1, plan.VideoMaxRate / 2);
        Assert.Equal(plan.AudioBitrate, smaller.AudioBitrate);
    }

    [Fact]
    public void Impossible_length_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EncodePlanner.Plan(new Size(1920, 1080), 60, 10 * 3600, true, TwentyMB));
    }
}
