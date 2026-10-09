using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Snipperoo.Native;

namespace Snipperoo;

/// <summary>The tray's right-click menu, drawn dark with icon glyphs to match the app's windows.</summary>
internal static class TrayMenu
{
    private static readonly Color Background = Color.FromArgb(24, 25, 34);
    private static readonly Color Hover = Color.FromArgb(38, 40, 56);
    private static readonly Color Text = Color.FromArgb(244, 244, 248);
    private static readonly Color Muted = Color.FromArgb(160, 162, 184);
    private static readonly Color Stroke = Color.FromArgb(43, 45, 60);

    /// <summary>A menu with a muted header line followed by the given (glyph, text, action) items; null is a separator.</summary>
    public static ContextMenuStrip Create(string header, params (string Glyph, string Text, Action OnClick)?[] items)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkRenderer(),
            Font = new Font("Segoe UI", 10f),
            ShowImageMargin = true,
            Padding = new Padding(4, 6, 4, 6),
        };
        // Windows 11 rounds the popup like its own menus; older versions ignore this.
        menu.Opened += (_, _) => Win32.SetDwmAttribute(menu.Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, Win32.DWMWCP_ROUNDSMALL);

        menu.Items.Add(new ToolStripLabel(header) { ForeColor = Muted, Font = new Font("Segoe UI", 8.5f), Padding = new Padding(2, 2, 12, 6) });
        foreach (var item in items)
        {
            if (item is not { } i)
            {
                menu.Items.Add(new ToolStripSeparator());
                continue;
            }
            var menuItem = new ToolStripMenuItem(i.Text, GlyphImage(i.Glyph, menu.DeviceDpi), (_, _) => i.OnClick())
            {
                Padding = new Padding(4, 7, 16, 7),
            };
            menu.Items.Add(menuItem);
        }
        return menu;
    }

    // Centred on the glyph's drawn outline: icon fonts have line metrics that push DrawString's centring off.
    private static Bitmap GlyphImage(string glyph, int dpi)
    {
        int size = 16 * dpi / 96;
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        using var family = new FontFamily(IconFontName);
        using var path = new GraphicsPath();
        path.AddString(glyph, family, (int)FontStyle.Regular, 14f * dpi / 96f, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        using var move = new Matrix();
        move.Translate((size - bounds.Width) / 2 - bounds.X, (size - bounds.Height) / 2 - bounds.Y);
        path.Transform(move);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Muted);
        g.FillPath(brush, path);
        return bitmap;
    }

    private static string IconFontName { get; } =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    private sealed class DarkRenderer() : ToolStripProfessionalRenderer(new DarkColors())
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Stroke);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Same colour as the menu; no separate gutter.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(rect, 6);
            using var brush = new SolidBrush(Hover);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem)
            {
                e.TextColor = Text;
                // With extra item padding the default layout puts the text near the top; centre it on the item.
                e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
                e.TextFormat |= TextFormatFlags.VerticalCenter;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Stroke);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Fallback colours for anything the renderer does not draw itself.
    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Stroke;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color SeparatorDark => Stroke;
        public override Color SeparatorLight => Stroke;
    }
}
