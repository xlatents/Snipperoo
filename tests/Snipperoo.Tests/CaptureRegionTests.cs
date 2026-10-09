using System.Drawing;
using Snipperoo.Capture;

namespace Snipperoo.Tests;

public class CaptureRegionTests
{
    private static MonitorOutput Monitor(Rectangle bounds, int turns = 0) => new(@"\\.\DISPLAY1", bounds, 0, 0, turns);

    [Fact]
    public void Clips_to_monitor_and_rounds_to_even()
    {
        var monitor = Monitor(new Rectangle(-1080, -486, 1080, 1920));
        var region = CaptureRegion.Create(new Rectangle(-1200, -400, 501, 333), monitor)!;

        Assert.Equal(new Rectangle(-1080, -400, 380, 332), region.Bounds);
    }

    [Fact]
    public void Too_small_after_clipping_is_null()
    {
        var monitor = Monitor(new Rectangle(0, 0, 2560, 1440));

        Assert.Null(CaptureRegion.Create(new Rectangle(2550, 100, 300, 300), monitor));
    }

    [Fact]
    public void Unrotated_crop_is_monitor_local()
    {
        var region = CaptureRegion.Create(new Rectangle(2660, 366, 640, 480), Monitor(new Rectangle(2560, 266, 1920, 1080)))!;

        Assert.Equal(new Rectangle(100, 100, 640, 480), region.FramebufferCrop);
        Assert.Null(region.UprightFilter);
    }

    // Desktop 1080x1920 (portrait), framebuffer 1920x1080.
    [Theory]
    [InlineData(1, 1920 - 200 - 300, 100, 300, 400, "transpose=cclock")]
    [InlineData(2, 1080 - 100 - 400, 1920 - 200 - 300, 400, 300, "hflip,vflip")]
    [InlineData(3, 200, 1080 - 100 - 400, 300, 400, "transpose=clock")]
    public void Rotated_crop_maps_into_framebuffer(int turns, int x, int y, int w, int h, string filter)
    {
        var size = new Size(1080, 1920);
        var local = new Rectangle(100, 200, 400, 300);

        var crop = CaptureRegion.ToFramebuffer(local, size, turns);

        Assert.Equal(new Rectangle(x, y, w, h), crop);
        var region = CaptureRegion.Create(local, Monitor(new Rectangle(Point.Empty, size), turns))!;
        Assert.Equal(filter, region.UprightFilter);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Rotated_crop_of_full_monitor_is_full_framebuffer(int turns)
    {
        var desktop = new Size(1080, 1920);
        var framebuffer = turns == 2 ? desktop : new Size(desktop.Height, desktop.Width);

        var crop = CaptureRegion.ToFramebuffer(new Rectangle(Point.Empty, desktop), desktop, turns);

        Assert.Equal(new Rectangle(Point.Empty, framebuffer), crop);
    }
}
