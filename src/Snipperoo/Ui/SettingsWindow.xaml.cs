using System.Windows;
using Microsoft.Win32;
using Snipperoo.Encoding;
using Snipperoo.Setup;

namespace Snipperoo.Ui;

internal enum SettingsPage { General, Shortcuts, Capture }

/// <summary>Settings, saved on every change. The owner unregisters its hotkeys while this window is open.</summary>
internal partial class SettingsWindow : ThemedWindow
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        VersionText.Text = $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";

        Plan.Plan = settings.Plan;
        AutoStart.IsOn = Installer.StartWithWindows;
        FolderText.Text = settings.OutputFolder;
        Shortcuts.Load(settings);
        Audio.IsOn = settings.RecordSystemAudio;
        CursorSwitch.IsOn = settings.ShowCursor;

        Plan.PlanChanged += plan => Save(() => _settings.Plan = plan);
        AutoStart.Toggled += on => Installer.StartWithWindows = on;
        Shortcuts.Changed += () => Save(() => Shortcuts.SaveTo(_settings));
        Audio.Toggled += on => Save(() => _settings.RecordSystemAudio = on);
        CursorSwitch.Toggled += on => Save(() => _settings.ShowCursor = on);
        Engine.Ready += ffmpeg => EngineReady?.Invoke(ffmpeg);
        Loaded += (_, _) => Engine.Start(settings.FfmpegPath);
    }

    /// <summary>The user confirmed uninstalling; the owner should clean up and exit.</summary>
    public event Action? UninstallRequested;

    public event Action<Ffmpeg>? EngineReady;

    public void ShowPage(SettingsPage page) =>
        (page switch
        {
            SettingsPage.Shortcuts => ShortcutsNav,
            SettingsPage.Capture => CaptureNav,
            _ => GeneralNav,
        }).IsChecked = true;

    private void Save(Action change)
    {
        change();
        _settings.Save();
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        // Checked fires during InitializeComponent, before the pages exist.
        if (GeneralPage is null)
            return;
        GeneralPage.Visibility = GeneralNav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = ShortcutsNav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CapturePage.Visibility = CaptureNav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Where should clips and screenshots go?", InitialDirectory = _settings.OutputFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        Save(() => _settings.OutputFolder = dialog.FolderName);
        FolderText.Text = dialog.FolderName;
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (ConfirmDialog.Show(this, "Uninstall Snipperoo?",
                "This removes the app and its settings. Your clips and screenshots stay where they are.", "Uninstall"))
        {
            UninstallRequested?.Invoke();
        }
    }
}
