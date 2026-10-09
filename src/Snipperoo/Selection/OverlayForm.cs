using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Snipperoo.Capture;

namespace Snipperoo.Selection;

/// <summary>
/// Full-screen overlay for one monitor: a dimmed screenshot with the highlighted area shown at full brightness.
/// Works in monitor-local physical pixels; converts to desktop coordinates only when talking to the session.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int DragThreshold = 4;
    private const int WM_DPICHANGED = 0x02E0;
    // Brand violet, as in Ui/Theme.xaml.
    private static readonly Color AccentColor = Color.FromArgb(155, 123, 255);
    private static readonly Color DimColor = Color.FromArgb(120, 0, 0, 0);

    private readonly SelectionSession _session;
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmed;
    private readonly Font _font;
    private readonly string _hintText;
    private Rectangle? _highlight;
    private Point? _dragStart;
    private bool _dragging;

    public MonitorOutput Monitor { get; }

    public OverlayForm(SelectionSession session, MonitorOutput monitor)
    {
        _session = session;
        Monitor = monitor;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = monitor.DesktopBounds;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;

        _screenshot = CaptureScreen(monitor.DesktopBounds);
        _dimmed = Dim(_screenshot);
        _font = new Font("Segoe UI", 13f * DeviceDpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
        string action = session.Purpose == SelectionPurpose.Screenshot ? "screenshot" : "record";
        _hintText = $"Click to {action}  •  Drag to mark an area  •  Right-click or Esc to cancel";
    }

    /// <summary>Highlights the window or monitor under <paramref name="screenPoint"/>.</summary>
    public void HoverAt(Point screenPoint) =>
        SetHighlight(ToLocal(_session.TargetAt(screenPoint, Monitor)));

    public void ClearHighlight() => SetHighlight(null);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _session.Finish(null);
        else if (e.Button == MouseButtons.Left)
            _dragStart = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _session.Activate(this);

        if (_dragStart is { } start && (e.Button & MouseButtons.Left) != 0)
        {
            _dragging |= Math.Abs(e.X - start.X) > DragThreshold || Math.Abs(e.Y - start.Y) > DragThreshold;
            if (_dragging)
            {
                SetHighlight(Rectangle.Intersect(FromPoints(start, e.Location), ClientRectangle));
                return;
            }
        }

        HoverAt(PointToScreen(e.Location));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragStart is null)
            return;

        // A click picks the hovered target; a drag picks the dragged rectangle. Both are in _highlight.
        if (_highlight is not { Width: > 0, Height: > 0 } local)
        {
            _session.Finish(null);
            return;
        }

        var image = _session.Purpose == SelectionPurpose.Screenshot
            ? _screenshot.Clone(local, PixelFormat.Format32bppArgb)
            : null;
        _session.Finish(new SelectedArea(Monitor, ToDesktop(local), image));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
            _session.Finish(null);
    }

    protected override void WndProc(ref Message m)
    {
        // The form is created on its own monitor at physical size; never let WinForms rescale it.
        if (m.Msg == WM_DPICHANGED)
            return;
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_dimmed, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);

        if (_highlight is { } rect)
        {
            var bright = Rectangle.Intersect(rect, e.ClipRectangle);
            if (!bright.IsEmpty)
                g.DrawImage(_screenshot, bright, bright, GraphicsUnit.Pixel);
        }

        g.CompositingMode = CompositingMode.SourceOver;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        DrawLabel(g, _hintText, new Point(ClientSize.Width / 2, LabelPadding * 3), centered: true);

        if (_highlight is { } highlight)
        {
            using var pen = new Pen(AccentColor, 2) { Alignment = PenAlignment.Inset };
            g.DrawRectangle(pen, highlight);
            DrawLabel(g, $"{highlight.Width & ~1} × {highlight.Height & ~1}", SizeLabelOrigin(highlight), centered: false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshot.Dispose();
            _dimmed.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }

    private int LabelPadding => 6 * DeviceDpi / 96;

    private void SetHighlight(Rectangle? rect)
    {
        if (rect == _highlight)
            return;
        // Repaint the old and new areas, including the border and the size label below/above them.
        InvalidateAround(_highlight);
        _highlight = rect;
        InvalidateAround(_highlight);
    }

    private void InvalidateAround(Rectangle? rect)
    {
        if (rect is not { } r)
            return;
        int labelSpace = _font.Height + LabelPadding * 4;
        r.Inflate(4, labelSpace);
        r.Width += 200 * DeviceDpi / 96; // size label can be wider than a narrow selection
        Invalidate(r);
    }

    private Point SizeLabelOrigin(Rectangle highlight)
    {
        int labelHeight = _font.Height + LabelPadding * 2;
        bool fitsBelow = highlight.Bottom + LabelPadding + labelHeight <= ClientSize.Height;
        return fitsBelow
            ? new Point(highlight.Left, highlight.Bottom + LabelPadding)
            : new Point(highlight.Left + LabelPadding, highlight.Bottom - LabelPadding - labelHeight); // inside, e.g. full monitor
    }

    private void DrawLabel(Graphics g, string text, Point origin, bool centered)
    {
        var size = TextRenderer.MeasureText(g, text, _font);
        var box = new Rectangle(origin, new Size(size.Width + LabelPadding * 4, size.Height + LabelPadding * 2));
        if (centered)
            box.X -= box.Width / 2;
        using var background = new SolidBrush(Color.FromArgb(225, 24, 25, 34));
        using var pill = RoundedRect(box, box.Height / 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(background, pill);
        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, text, _font, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Rectangle ToLocal(Rectangle desktop) =>
        desktop with { X = desktop.X - Monitor.DesktopBounds.X, Y = desktop.Y - Monitor.DesktopBounds.Y };

    private Rectangle ToDesktop(Rectangle local) =>
        local with { X = local.X + Monitor.DesktopBounds.X, Y = local.Y + Monitor.DesktopBounds.Y };

    private static Rectangle FromPoints(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }

    private static Bitmap Dim(Bitmap source)
    {
        var dimmed = (Bitmap)source.Clone();
        using var g = Graphics.FromImage(dimmed);
        using var brush = new SolidBrush(DimColor);
        g.FillRectangle(brush, 0, 0, dimmed.Width, dimmed.Height);
        return dimmed;
    }
}
