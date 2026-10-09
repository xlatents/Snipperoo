using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Snipperoo.Native;

namespace Snipperoo.Ui;

/// <summary>
/// Base for the app's windows: no system title bar (each window draws <see cref="TitleBar"/>), dark theme,
/// and Windows 11 rounded corners with the normal DWM shadow.
/// </summary>
internal class ThemedWindow : Window
{
    public ThemedWindow()
    {
        // Keeping the standard style (not WindowStyle.None) is what keeps the DWM shadow and rounded corners;
        // WindowChrome then hides the system title bar.
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("BgBrush");
        Icon = AppAssets.Icon;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TitleBar.BarHeight,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetDwmAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Win32.SetDwmAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, Win32.DWMWCP_ROUND);
        var stroke = (Color)FindResource("StrokeColor");
        Win32.SetDwmAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, stroke.R | stroke.G << 8 | stroke.B << 16); // COLORREF: 0x00BBGGRR
    }
}

/// <summary>The app logo, loaded from the embedded .ico.</summary>
internal static class AppAssets
{
    private static readonly Uri IconUri = new("pack://application:,,,/Snipperoo;component/Assets/snipperoo.ico");

    public static BitmapFrame Icon { get; } = BitmapFrame.Create(IconUri);

    /// <summary>The 256 px frame, for drawing the logo larger than an icon.</summary>
    public static BitmapSource Logo { get; } =
        new IconBitmapDecoder(IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.MaxBy(f => f.PixelWidth)!;
}
