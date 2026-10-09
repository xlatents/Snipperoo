using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snipperoo.Native;
using Keys = System.Windows.Forms.Keys;

namespace Snipperoo.Ui;

/// <summary>
/// Shows a shortcut as keycaps. Click it and press a key combination to change it; Esc cancels.
/// <see cref="Validate"/> can reject a combination (e.g. taken by another app) with a message.
/// </summary>
internal partial class HotkeyBox : UserControl
{
    private Hotkey _value;
    private bool _recording;

    public HotkeyBox()
    {
        InitializeComponent();
        MouseEnter += (_, _) => UpdateBorder();
        MouseLeave += (_, _) => UpdateBorder();
    }

    /// <summary>Returns an error message to reject a combination, or null to accept it.</summary>
    public Func<Hotkey, string?>? Validate { get; set; }

    public event Action<Hotkey>? HotkeyChanged;

    public Hotkey Value
    {
        get => _value;
        set
        {
            _value = value;
            ShowKeys(value.DisplayParts);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Focus();
        StartRecording();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            if (e.Key is Key.Enter or Key.Space)
                StartRecording();
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (IsModifier(key))
        {
            ShowHeldModifiers();
            return;
        }

        var mods = Keyboard.Modifiers;
        var candidate = new Hotkey(
            Hotkey.ModifierFlags(
                mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt),
                mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Windows)),
            (Keys)KeyInterop.VirtualKeyFromKey(key));

        string? error = candidate.HasModifier ? Validate?.Invoke(candidate) : "Add Ctrl, Alt, Shift or Win";
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        StopRecording();
        Value = candidate;
        HotkeyChanged?.Invoke(candidate);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (!_recording)
            return;
        e.Handled = true;
        ShowHeldModifiers();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_recording)
            StopRecording();
    }

    private void StartRecording()
    {
        _recording = true;
        ErrorText.Visibility = Visibility.Collapsed;
        ShowHeldModifiers();
        UpdateBorder();
    }

    private void StopRecording()
    {
        _recording = false;
        ErrorText.Visibility = Visibility.Collapsed;
        Value = _value;
        UpdateBorder();
    }

    private void ShowHeldModifiers()
    {
        var mods = Keyboard.Modifiers;
        var held = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) held.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) held.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) held.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) held.Add("Win");
        ShowKeys(held);
    }

    private void ShowKeys(IReadOnlyList<string> keys)
    {
        KeyList.ItemsSource = keys;
        Prompt.Visibility = _recording && keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ShowHeldModifiers();
    }

    private void UpdateBorder() =>
        Box.BorderBrush = (Brush)FindResource(_recording ? "AccentBrush" : IsMouseOver ? "FaintBrush" : "StrokeBrush");

    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
