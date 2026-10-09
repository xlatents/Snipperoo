using System.ComponentModel;
using System.Windows.Forms;

namespace Snipperoo.Native;

/// <summary>Registers one global hotkey on a hidden message window and raises <see cref="Pressed"/> on the UI thread.</summary>
internal sealed class HotkeyListener : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;

    public event Action? Pressed;

    /// <summary>Throws Win32Exception if another app already owns the combination.</summary>
    public HotkeyListener(Hotkey hotkey)
    {
        CreateHandle(new CreateParams());
        if (!Win32.RegisterHotKey(Handle, HotkeyId, hotkey.Modifiers | Win32.MOD_NOREPEAT, (uint)hotkey.Key))
        {
            var error = new Win32Exception();
            DestroyHandle();
            throw error;
        }
    }

    /// <summary>True if no other app (and no listener of ours) currently owns <paramref name="hotkey"/>.</summary>
    public static bool IsAvailable(Hotkey hotkey)
    {
        try
        {
            new HotkeyListener(hotkey).Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY && m.WParam == HotkeyId)
            Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == 0)
            return;
        Win32.UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }
}
