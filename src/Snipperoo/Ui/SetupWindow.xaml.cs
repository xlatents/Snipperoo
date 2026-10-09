using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Snipperoo.Ui;

/// <summary>
/// First-run wizard: Discord plan → shortcuts → done (autostart, video engine). The video engine is set up in the
/// background from the start, so it is usually ready by the last step.
/// </summary>
internal partial class SetupWindow : ThemedWindow
{
    private readonly AppSettings _settings;
    private readonly StackPanel[] _pages;
    private int _page;

    public SetupWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _pages = [PlanPage, ShortcutsPage, DonePage];

        Plan.Plan = settings.Plan;
        Shortcuts.Load(settings);
        Engine.Finished += UpdateButtons;
        Loaded += (_, _) => Engine.Start(settings.FfmpegPath);
        ShowPage(0, animate: false);
    }

    /// <summary>Set when the user finished the wizard (rather than closing it).</summary>
    public bool Completed { get; private set; }

    public bool StartWithWindows => AutoStart.IsOn;

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_page < _pages.Length - 1)
        {
            ShowPage(_page + 1, animate: true);
            return;
        }

        _settings.Plan = Plan.Plan;
        Shortcuts.SaveTo(_settings);
        Completed = true;
        Close();
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowPage(_page - 1, animate: true);

    private void ShowPage(int index, bool animate)
    {
        bool forward = index > _page;
        _page = index;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        Border[] dots = [Dot1, Dot2, Dot3];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Width = i == index ? 40 : 18;
            dots[i].Background = (Brush)FindResource(i <= index ? "AccentBrush" : "SurfaceHiBrush");
        }
        StepText.Text = $"Step {index + 1} of {_pages.Length}";
        UpdateButtons();

        if (animate)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(260);
            PageShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(forward ? 36 : -36, 0, duration) { EasingFunction = ease });
            Pages.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        }
    }

    private void UpdateButtons()
    {
        bool last = _page == _pages.Length - 1;
        BackButton.Visibility = _page > 0 ? Visibility.Visible : Visibility.Hidden;
        NextButton.Content = last ? (Engine.IsBusy ? "Getting ready…" : "Start Snipperoo") : "Continue";
        NextButton.IsEnabled = !last || !Engine.IsBusy;
    }
}
