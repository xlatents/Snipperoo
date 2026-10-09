using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Snipperoo.Native;

namespace Snipperoo;

/// <summary>Tray icons: the app logo, plus a status dot for recording (red) and encoding (amber).</summary>
internal sealed class TrayIcons : IDisposable
{
    private static readonly Uri IconUri = new("pack://application:,,,/Snipperoo;component/Assets/snipperoo.ico");

    public Icon Idle { get; }
    public Icon Recording { get; }
    public Icon Encoding { get; }

    public TrayIcons()
    {
        using var stream = System.Windows.Application.GetResourceStream(IconUri)!.Stream;
        Idle = new Icon(stream, SystemInformation.SmallIconSize);
        Recording = WithBadge(Idle, Color.FromArgb(255, 72, 72));
        Encoding = WithBadge(Idle, Color.FromArgb(255, 184, 48));
    }

    private static Icon WithBadge(Icon icon, Color color)
    {
        using var bitmap = icon.ToBitmap();
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = bitmap.Width * 0.5f;
            var dot = new RectangleF(bitmap.Width - size, bitmap.Height - size, size, size);
            using var ring = new SolidBrush(Color.FromArgb(15, 16, 21));
            g.FillEllipse(ring, dot);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, RectangleF.Inflate(dot, -bitmap.Width / 16f, -bitmap.Height / 16f));
        }

        // FromHandle does not own the HICON; clone so the handle can be released immediately.
        nint hIcon = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            Win32.DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        Idle.Dispose();
        Recording.Dispose();
        Encoding.Dispose();
    }
}
