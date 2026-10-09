using System.Drawing;

namespace Snipperoo.Capture;

/// <summary>
/// A recordable rectangle on one monitor. <see cref="Bounds"/> is in desktop coordinates (upright),
/// <see cref="FramebufferCrop"/> is the same area in the possibly rotated ddagrab framebuffer.
/// </summary>
internal sealed record CaptureRegion(MonitorOutput Monitor, Rectangle Bounds, Rectangle FramebufferCrop)
{
    /// <summary>Smallest region worth recording; encoders also reject tiny frames.</summary>
    public const int MinSize = 32;

    /// <summary>
    /// Clips <paramref name="requested"/> to <paramref name="monitor"/> and rounds the size down to even numbers
    /// (4:2:0 video needs them). Returns null if too little is left.
    /// </summary>
    public static CaptureRegion? Create(Rectangle requested, MonitorOutput monitor)
    {
        var clipped = Rectangle.Intersect(requested, monitor.DesktopBounds);
        clipped.Width &= ~1;
        clipped.Height &= ~1;
        if (clipped.Width < MinSize || clipped.Height < MinSize)
            return null;

        var local = clipped with { X = clipped.X - monitor.DesktopBounds.X, Y = clipped.Y - monitor.DesktopBounds.Y };
        var crop = ToFramebuffer(local, monitor.DesktopBounds.Size, monitor.QuarterTurns);
        return new CaptureRegion(monitor, clipped, crop);
    }

    /// <summary>ffmpeg filter that turns the captured framebuffer crop upright, or null if none is needed.</summary>
    public string? UprightFilter => Monitor.QuarterTurns switch
    {
        1 => "transpose=cclock",
        2 => "hflip,vflip",
        3 => "transpose=clock",
        _ => null,
    };

    // Maps a monitor-local rectangle into a framebuffer that is the desktop turned clockwise `turns` times.
    internal static Rectangle ToFramebuffer(Rectangle r, Size desktop, int turns) => turns switch
    {
        1 => new Rectangle(desktop.Height - r.Bottom, r.X, r.Height, r.Width),
        2 => new Rectangle(desktop.Width - r.Right, desktop.Height - r.Bottom, r.Width, r.Height),
        3 => new Rectangle(r.Y, desktop.Width - r.Right, r.Height, r.Width),
        _ => r,
    };
}
