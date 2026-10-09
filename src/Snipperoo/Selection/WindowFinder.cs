using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snipperoo.Native;

namespace Snipperoo.Selection;

/// <summary>Snapshot of the top-level windows a user would consider "a window", in z-order (topmost first).</summary>
internal static unsafe class WindowFinder
{
    // Desktop and taskbar: hovering them should select the whole monitor instead.
    private static readonly HashSet<string> IgnoredClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static IReadOnlyList<Rectangle> GetWindowBounds()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            Win32.EnumWindows(&Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        uint ownProcess = (uint)Environment.ProcessId;
        var bounds = new List<Rectangle>();
        foreach (nint hWnd in handles)
        {
            if (IsCandidate(hWnd, ownProcess) && Win32.GetFrameBounds(hWnd) is { IsEmpty: false } rect)
                bounds.Add(rect);
        }
        return bounds;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hWnd, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(hWnd);
        return 1;
    }

    private static bool IsCandidate(nint hWnd, uint ownProcess)
    {
        if (!Win32.IsWindowVisible(hWnd) || Win32.IsIconic(hWnd) || Win32.IsCloaked(hWnd))
            return false;

        // Tool windows and click-through overlays (game/Discord/GPU overlays) are not recording targets.
        long exStyle = Win32.GetWindowLongPtr(hWnd, Win32.GWL_EXSTYLE);
        if ((exStyle & (Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT)) != 0)
            return false;

        Win32.GetWindowThreadProcessId(hWnd, out uint pid);
        return pid != ownProcess && !IgnoredClasses.Contains(Win32.GetClassName(hWnd));
    }
}
