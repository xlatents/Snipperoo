using System.Windows.Controls;
using Snipperoo.Native;

namespace Snipperoo.Ui;

/// <summary>
/// The record and screenshot shortcuts, each rejected if it equals the other or another app owns it.
/// The app's own hotkeys must be unregistered while this is shown, or they would read as taken.
/// </summary>
internal partial class ShortcutsEditor : UserControl
{
    public ShortcutsEditor()
    {
        InitializeComponent();
        RecordBox.Validate = candidate => Check(candidate, ScreenshotBox.Value, "take screenshots");
        ScreenshotBox.Validate = candidate => Check(candidate, RecordBox.Value, "record");
        RecordBox.HotkeyChanged += _ => Changed?.Invoke();
        ScreenshotBox.HotkeyChanged += _ => Changed?.Invoke();
    }

    public event Action? Changed;

    /// <summary>Fills both boxes from settings; unparsable values fall back to the defaults.</summary>
    public void Load(AppSettings settings)
    {
        var defaults = new AppSettings();
        RecordBox.Value = ParseOr(settings.RecordHotkey, defaults.RecordHotkey);
        ScreenshotBox.Value = ParseOr(settings.ScreenshotHotkey, defaults.ScreenshotHotkey);
    }

    public void SaveTo(AppSettings settings)
    {
        settings.RecordHotkey = RecordBox.Value.ToString();
        settings.ScreenshotHotkey = ScreenshotBox.Value.ToString();
    }

    public Hotkey RecordHotkey => RecordBox.Value;
    public Hotkey ScreenshotHotkey => ScreenshotBox.Value;

    private static string? Check(Hotkey candidate, Hotkey other, string otherUse) =>
        candidate == other ? $"Already used to {otherUse}"
        : HotkeyListener.IsAvailable(candidate) ? null
        : "Another app already uses this";

    private static Hotkey ParseOr(string text, string fallback)
    {
        try
        {
            return Hotkey.Parse(text);
        }
        catch (FormatException)
        {
            return Hotkey.Parse(fallback);
        }
    }
}
