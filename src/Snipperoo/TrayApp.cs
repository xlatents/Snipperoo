using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Snipperoo.Capture;
using Snipperoo.Encoding;
using Snipperoo.Native;
using Snipperoo.Output;
using Snipperoo.Selection;
using Snipperoo.Setup;
using Snipperoo.Ui;

namespace Snipperoo;

/// <summary>
/// The running app: a tray icon plus two global hotkeys. Record: Idle → Selecting → Recording → Idle, with encoding in
/// the background so a new recording can start right away. Screenshot: Idle → Selecting → saved and copied → Idle.
/// </summary>
internal sealed class TrayApp : IDisposable
{
    private enum State { Idle, Selecting, Recording, Stopping }

    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly TrayIcons _icons = new();
    private Ffmpeg? _ffmpeg;
    private Task<LiveEncoder>? _liveEncoder;
    private HotkeyListener? _recordHotkey;
    private HotkeyListener? _screenshotHotkey;

    private State _state = State.Idle;
    private SelectionSession? _selection;
    private Recorder? _recorder;
    private RecordingFrame? _frame;
    private SettingsWindow? _settingsWindow;
    private int _encodesRunning;

    /// <param name="welcome">Show a "ready" toast; used right after setup.</param>
    public TrayApp(AppSettings settings, bool welcome)
    {
        _settings = settings;
        _tray = new NotifyIcon { Icon = _icons.Idle, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            // The red icon doubles as a stop button.
            if (_state == State.Recording && _recorder is not null)
                _ = StopRecordingAsync();
            else
                ShowSettings();
        };

        UseFfmpeg(Ffmpeg.Locate(settings.FfmpegPath));
        RegisterHotkeys();
        UpdateTray();
        UpdateMenu();

        if (_ffmpeg is null)
            _ = SetUpFfmpegAsync();
        else if (welcome)
            Toast.Show("Snipperoo is ready",
                $"{Display(settings.RecordHotkey)} to record, {Display(settings.ScreenshotHotkey)} for a screenshot. I'm in your tray.",
                onClick: ShowSettings, hint: "Click to open Settings");
        Log.Info("Started");
    }

    /// <summary>Raised when the app should exit (menu Exit, or after uninstalling).</summary>
    public event Action? ExitRequested;

    public void ShowSettings() => ShowSettings(SettingsPage.General);

    private void ShowSettings(SettingsPage page)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.ShowPage(page);
            _settingsWindow.Activate();
            return;
        }

        // Unregister our hotkeys so the shortcut editor can test combinations, and so pressing one does not start a capture.
        UnregisterHotkeys();
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.EngineReady += UseFfmpeg;
        _settingsWindow.UninstallRequested += () =>
        {
            Installer.Uninstall();
            ExitRequested?.Invoke();
        };
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            RegisterHotkeys();
            UpdateTray();
            UpdateMenu();
        };
        _settingsWindow.ShowPage(page);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // Runs on start when ffmpeg is missing, e.g. when setup could not get it; screenshots work meanwhile.
    private async Task SetUpFfmpegAsync()
    {
        Toast.Show("Getting the video engine ready", "A one-time download of ffmpeg. Screenshots already work.");
        try
        {
            UseFfmpeg(await FfmpegInstaller.EnsureAsync(_settings.FfmpegPath));
            Toast.Show("Ready to record", $"{Display(_settings.RecordHotkey)} to record a clip.");
        }
        catch (Exception ex)
        {
            Log.Error("Video engine setup failed", ex);
            ShowError("Video engine missing", $"{ex.Message} Click to retry in Settings.", () => ShowSettings(SettingsPage.Capture));
        }
    }

    private void UseFfmpeg(Ffmpeg? ffmpeg)
    {
        if (ffmpeg is null || _ffmpeg?.FfmpegPath == ffmpeg.FfmpegPath)
            return;
        _ffmpeg = ffmpeg;
        _liveEncoder = LiveEncoder.DetectAsync(ffmpeg);
    }

    private void RegisterHotkeys()
    {
        _recordHotkey = RegisterHotkey(_settings.RecordHotkey, OnRecordHotkey);
        _screenshotHotkey = RegisterHotkey(_settings.ScreenshotHotkey, OnScreenshotHotkey);
    }

    private void UnregisterHotkeys()
    {
        _recordHotkey?.Dispose();
        _screenshotHotkey?.Dispose();
        _recordHotkey = _screenshotHotkey = null;
    }

    private HotkeyListener? RegisterHotkey(string text, Action onPressed)
    {
        try
        {
            var listener = new HotkeyListener(Hotkey.Parse(text));
            listener.Pressed += onPressed;
            return listener;
        }
        catch (Exception ex) when (ex is FormatException or Win32Exception)
        {
            Log.Error($"Hotkey '{text}' unavailable", ex);
            ShowError("Shortcut unavailable", $"Another app is using {Display(text)}. Click to pick a different one.",
                () => ShowSettings(SettingsPage.Shortcuts));
            return null;
        }
    }

    private void OnRecordHotkey()
    {
        switch (_state)
        {
            case State.Idle:
                StartSelection(SelectionPurpose.Record);
                break;
            case State.Selecting:
                _selection?.Finish(null);
                break;
            case State.Recording when _recorder is not null:
                _ = StopRecordingAsync();
                break;
        }
    }

    // Ignored while recording: the overlay would end up in the video.
    private void OnScreenshotHotkey()
    {
        switch (_state)
        {
            case State.Idle:
                StartSelection(SelectionPurpose.Screenshot);
                break;
            case State.Selecting:
                _selection?.Finish(null);
                break;
        }
    }

    private void StartSelection(SelectionPurpose purpose)
    {
        if (purpose == SelectionPurpose.Record && _ffmpeg is null)
        {
            ShowError("Video engine not ready", "Recording needs ffmpeg. Click to check on it in Settings.",
                () => ShowSettings(SettingsPage.Capture));
            return;
        }

        try
        {
            _selection = new SelectionSession(MonitorOutput.Enumerate(), purpose);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start selection", ex);
            ShowError("Could not start", ex.Message);
            return;
        }

        _selection.Completed += selection =>
        {
            _selection = null;
            _state = State.Idle;
            if (selection is null)
                return;

            if (purpose == SelectionPurpose.Screenshot)
                SaveScreenshot(selection);
            else if (CaptureRegion.Create(selection.Bounds, selection.Monitor) is { } region)
                _ = StartRecordingAsync(region);
            else
                ShowError("Area too small", $"Pick an area of at least {CaptureRegion.MinSize}×{CaptureRegion.MinSize} pixels.");
        };
        _state = State.Selecting;
        _selection.Show();
    }

    private void SaveScreenshot(SelectedArea selection)
    {
        using var image = selection.Image!;
        try
        {
            Directory.CreateDirectory(_settings.ScreenshotFolder);
            string path = Path.Combine(_settings.ScreenshotFolder, $"Snipperoo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
            image.Save(path, ImageFormat.Png);
            ClipboardFile.Copy(path, image);

            Log.Info($"Saved {path}");
            Toast.Show("Screenshot copied", $"{image.Width} × {image.Height}  ·  paste it anywhere",
                Thumbnails.FromBitmap(image), onClick: () => ShowInFolder(path), hint: "Click to show in folder");
        }
        catch (Exception ex)
        {
            Log.Error("Could not save screenshot", ex);
            ShowError("Screenshot failed", ex.Message);
        }
    }

    private async Task StartRecordingAsync(CaptureRegion region)
    {
        _state = State.Recording;
        try
        {
            var encoder = await _liveEncoder!;
            var recorder = Recorder.Start(region, _settings, _ffmpeg!, encoder);
            _recorder = recorder;
            _frame = new RecordingFrame(region);
            _frame.Show();
            UpdateTray();
            Log.Info($"Recording {region.Bounds} on {region.Monitor.DeviceName} with {encoder.Name}");

            await recorder.Exited;
            if (_recorder == recorder && _state == State.Recording)
            {
                // ffmpeg died on its own: bad crop, display mode change, GPU reset...
                Log.Error($"Recording stopped unexpectedly:{Environment.NewLine}{recorder.ErrorOutput}");
                ShowError("Recording stopped", "Something interrupted the recording. Details are in the log.");
                EndRecording();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not start recording", ex);
            ShowError("Could not start recording", ex.Message);
            EndRecording();
        }
    }

    private async Task StopRecordingAsync()
    {
        var recorder = _recorder!;
        _state = State.Stopping;
        CloseFrame();

        Recording recording;
        try
        {
            recording = await recorder.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not finish recording", ex);
            ShowError("Recording failed", "The recording could not be finished. Details are in the log.");
            EndRecording();
            return;
        }

        EndRecording();
        await EncodeAsync(recording);
    }

    private void EndRecording()
    {
        CloseFrame();
        _recorder?.Dispose();
        _recorder = null;
        _state = State.Idle;
        UpdateTray();
    }

    private void CloseFrame()
    {
        _frame?.Dispose();
        _frame = null;
    }

    private async Task EncodeAsync(Recording recording)
    {
        var ffmpeg = _ffmpeg!;
        _encodesRunning++;
        UpdateTray();
        try
        {
            var clip = await new ClipEncoder(ffmpeg).EncodeAsync(recording, _settings.OutputFolder, _settings.MaxFileSizeBytes);
            ClipboardFile.Copy(clip.Path);
            Directory.Delete(recording.WorkFolder, recursive: true);
            Log.Info($"Saved {clip.Path} ({clip.Bytes} bytes)");

            var plan = clip.Plan;
            var thumbnail = await Thumbnails.FromVideoAsync(ffmpeg, clip.Path, clip.Duration);
            Toast.Show("Clip copied, paste it in Discord",
                $"{clip.Bytes / 1e6:0.0} MB  ·  {clip.Duration:m\\:ss}  ·  {plan.Height}p{plan.FrameRate}",
                thumbnail, onClick: () => ShowInFolder(clip.Path), hint: "Click to show in folder");
        }
        catch (Exception ex)
        {
            Log.Error($"Encoding failed, raw recording kept in {recording.WorkFolder}", ex);
            ShowError("Couldn't finish the clip", "The raw recording was kept.");
        }
        finally
        {
            _encodesRunning--;
            UpdateTray();
        }
    }

    private void UpdateTray()
    {
        string record = Display(_settings.RecordHotkey);
        (_tray.Icon, _tray.Text) = (_state, _encodesRunning) switch
        {
            (State.Recording or State.Stopping, _) => (_icons.Recording, $"Snipperoo: recording. {record} or click to stop"),
            (_, > 0) => (_icons.Encoding, "Snipperoo: finishing your clip..."),
            _ => (_icons.Idle, "Snipperoo"),
        };
    }

    // The header shows the shortcuts, so this runs again whenever settings may have changed them.
    private void UpdateMenu()
    {
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = TrayMenu.Create(
            $"{Display(_settings.RecordHotkey)}  record   ·   {Display(_settings.ScreenshotHotkey)}  screenshot",
            ("", "Open folder", OpenFolder), // Folder
            ("", "Settings", ShowSettings), // Setting
            null,
            ("", "Exit", () => ExitRequested?.Invoke())); // PowerButton
        old?.Dispose();
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_settings.OutputFolder);
        Process.Start(new ProcessStartInfo(_settings.OutputFolder) { UseShellExecute = true });
    }

    private static void ShowInFolder(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

    /// <summary>Error toast; without <paramref name="onClick"/>, clicking opens the log.</summary>
    private static void ShowError(string title, string text, Action? onClick = null) =>
        Toast.Show(title, text, isError: true,
            onClick: onClick ?? (() => Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true })),
            hint: onClick is null ? "Click to open the log" : null);

    // "Alt+Shift+G" → "Alt + Shift + G" for messages.
    private static string Display(string hotkey) => hotkey.Replace("+", " + ");

    public void Dispose()
    {
        _selection?.Dispose();
        CloseFrame();
        _recorder?.Dispose();
        _settingsWindow?.Close();
        UnregisterHotkeys();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _icons.Dispose();
    }
}
