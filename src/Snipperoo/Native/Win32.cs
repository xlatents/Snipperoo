using System.Drawing;
using System.Runtime.InteropServices;

namespace Snipperoo.Native;

/// <summary>Win32 imports used by the app. All coordinates are physical pixels (the process is PerMonitorV2).</summary>
internal static unsafe partial class Win32
{
    public const int WM_HOTKEY = 0x0312;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const long WS_EX_LAYERED = 0x80000;
    public const long WS_EX_NOACTIVATE = 0x8000000;

    public const uint MOD_NOREPEAT = 0x4000;

    // Hides the window from screen capture (Desktop Duplication included). Windows 10 2004+.
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(nint hWnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint hWnd, uint affinity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint mapType);

    /// <summary>The character a key types on the current layout (e.g. OemMinus → '-'), or null for non-character keys.</summary>
    public static char? KeyToChar(System.Windows.Forms.Keys key)
    {
        const uint MAPVK_VK_TO_CHAR = 2;
        uint c = MapVirtualKey((uint)key, MAPVK_VK_TO_CHAR) & 0x7FFF; // top bit flags dead keys
        return c == 0 ? null : (char)c;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hWnd, int attribute, void* value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hWnd, int attribute, void* value, int size);

    /// <summary>Sets a DWM window attribute. Windows versions that do not know the attribute ignore it.</summary>
    public static void SetDwmAttribute(nint hWnd, int attribute, int value) =>
        DwmSetWindowAttribute(hWnd, attribute, &value, sizeof(int));

    public static string GetClassName(nint hWnd)
    {
        char* buffer = stackalloc char[256];
        int length = GetClassName(hWnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    /// <summary>Visible window bounds without the invisible resize border that GetWindowRect includes.</summary>
    public static Rectangle? GetFrameBounds(nint hWnd)
    {
        RECT rect;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, sizeof(RECT)) != 0)
            return null;
        return rect.ToRectangle();
    }

    /// <summary>True for windows DWM hides, e.g. UWP apps that are suspended or on another virtual desktop.</summary>
    public static bool IsCloaked(nint hWnd)
    {
        int cloaked;
        return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }
}
