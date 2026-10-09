using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Snipperoo.Native;

namespace Snipperoo.Ui;

/// <summary>
/// Small notification card in the bottom-right corner. Never takes focus, stays out of recordings, hides itself
/// after a few seconds (paused while hovered), and runs an action when clicked. Only one is shown at a time.
/// </summary>
internal partial class Toast : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private static Toast? _current;

    private readonly Action? _onClick;
    private bool _closing;

    private Toast(string title, string detail, ImageSource? thumbnail, bool isError, Action? onClick, string? hint)
    {
        InitializeComponent();
        _onClick = onClick;
        TitleText.Text = title;
        DetailText.Text = detail;
        HintText.Text = hint ?? "";
        HintText.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;

        if (thumbnail is not null)
        {
            Thumb.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
            Thumb.Visibility = Visibility.Visible;
            IconBadge.Visibility = Visibility.Collapsed;
        }
        IconGlyph.Text = isError ? "\uE7BA" : "\uE73E"; // Warning, CheckMark
        if (isError)
        {
            IconBadge.Background = (Brush)FindResource("DangerBrush");
            Countdown.Background = IconBadge.Background;
        }

        Loaded += (_, _) => Appear();
        MouseEnter += (_, _) => HoldCountdown();
        MouseLeave += (_, _) => StartCountdown();
    }

    /// <summary>
    /// Shows a toast, replacing any visible one. <paramref name="hint"/> is a small line saying what a click does.
    /// Must be called on the UI thread.
    /// </summary>
    public static void Show(string title, string detail, ImageSource? thumbnail = null, bool isError = false,
        Action? onClick = null, string? hint = null)
    {
        _current?.Disappear();
        _current = new Toast(title, detail, thumbnail, isError, onClick, hint);
        _current.Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (nint)(exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
        Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _onClick?.Invoke();
        Disappear();
    }

    private void Appear()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        double top = area.Bottom - ActualHeight;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(TopProperty, new DoubleAnimation(top + 24, top, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        StartCountdown();
    }

    private void StartCountdown()
    {
        var shrink = new DoubleAnimation(Card.ActualWidth - 32, 0, Lifetime);
        shrink.Completed += (_, _) =>
        {
            if (!IsMouseOver)
                Disappear();
        };
        Countdown.BeginAnimation(WidthProperty, shrink);
    }

    private void HoldCountdown()
    {
        double width = Countdown.ActualWidth;
        Countdown.BeginAnimation(WidthProperty, null); // drops back to the local value, so pin it first
        Countdown.Width = width;
    }

    private void Disappear()
    {
        if (_closing)
            return;
        _closing = true;
        if (_current == this)
            _current = null;

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
