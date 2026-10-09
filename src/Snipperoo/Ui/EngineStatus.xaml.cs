using System.Windows;
using System.Windows.Controls;
using Snipperoo.Encoding;
using Snipperoo.Setup;

namespace Snipperoo.Ui;

/// <summary>Finds or installs ffmpeg and shows progress, success, or the error with a retry button.</summary>
internal partial class EngineStatus : UserControl
{
    private string _configuredPath = "";

    public EngineStatus() => InitializeComponent();

    /// <summary>Raised on the UI thread once ffmpeg is available.</summary>
    public event Action<Ffmpeg>? Ready;

    /// <summary>Raised when a setup attempt finishes, successfully or not.</summary>
    public event Action? Finished;

    public bool IsBusy { get; private set; }
    public bool IsReady { get; private set; }

    public async void Start(string configuredPath)
    {
        _configuredPath = configuredPath;
        IsBusy = true;
        ShowState(busy: true, ok: false, "Setting up the video engine…", "A one-time download of ffmpeg. This can take a minute.");
        try
        {
            var ffmpeg = await FfmpegInstaller.EnsureAsync(configuredPath);
            IsReady = true;
            ShowState(busy: false, ok: true, "Video engine ready", "Recording and encoding are good to go.");
            Ready?.Invoke(ffmpeg);
        }
        catch (Exception ex)
        {
            Log.Error("Video engine setup failed", ex);
            ShowState(busy: false, ok: false, "Couldn't set up the video engine", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Finished?.Invoke();
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e) => Start(_configuredPath);

    private void ShowState(bool busy, bool ok, string headline, string detail)
    {
        Spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ReadyIcon.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        FailedIcon.Visibility = !busy && !ok ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = FailedIcon.Visibility;
        Headline.Text = headline;
        Detail.Text = detail;
    }
}
