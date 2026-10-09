using System.Drawing;
using System.Windows.Forms;
using Snipperoo.Native;

namespace Snipperoo.Capture;

/// <summary>Red outline around the area being recorded. Click-through and excluded from capture.</summary>
internal sealed class RecordingFrame : Form
{
    private const int Thickness = 3;
    private static readonly Color FrameColor = Color.FromArgb(235, 64, 52);

    public RecordingFrame(CaptureRegion region)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        // Outside the region where there is room; at a monitor edge it overlaps, which is fine since it is not captured.
        var bounds = region.Bounds;
        bounds.Inflate(Thickness, Thickness);
        Bounds = Rectangle.Intersect(bounds, region.Monitor.DesktopBounds);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_EXCLUDEFROMCAPTURE);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(FrameColor, Thickness) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset };
        e.Graphics.DrawRectangle(pen, ClientRectangle);
    }
}
