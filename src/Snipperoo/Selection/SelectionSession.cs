using System.Drawing;
using System.Windows.Forms;
using Snipperoo.Capture;
using Snipperoo.Native;

namespace Snipperoo.Selection;

/// <summary>
/// One area selection: an overlay per monitor over a frozen screenshot. Hovering highlights the window under the
/// cursor (or the whole monitor over the desktop), dragging marks a rectangle, left-click picks, right-click or
/// Esc cancels. Raises <see cref="Completed"/> exactly once.
/// </summary>
internal sealed class SelectionSession : IDisposable
{
    private readonly IReadOnlyList<Rectangle> _windows;
    private readonly List<OverlayForm> _overlays = [];
    private bool _finished;

    /// <summary>The user's pick, or null if they cancelled.</summary>
    public event Action<SelectedArea?>? Completed;

    public SelectionPurpose Purpose { get; }

    public SelectionSession(IReadOnlyList<MonitorOutput> monitors, SelectionPurpose purpose)
    {
        Purpose = purpose;

        // Snapshot windows before any overlay exists, so overlays never hit-test themselves.
        _windows = WindowFinder.GetWindowBounds();

        foreach (var monitor in monitors)
            _overlays.Add(new OverlayForm(this, monitor));
    }

    public void Show()
    {
        foreach (var overlay in _overlays)
            overlay.Show();

        // The hotkey gave us foreground rights; take focus so Esc reaches the overlay.
        var active = _overlays.FirstOrDefault(o => o.Monitor.DesktopBounds.Contains(Cursor.Position)) ?? _overlays[0];
        Win32.SetForegroundWindow(active.Handle);
        active.HoverAt(Cursor.Position);
    }

    /// <summary>Topmost window under <paramref name="point"/> clipped to its monitor, else the monitor itself.</summary>
    public Rectangle TargetAt(Point point, MonitorOutput monitor)
    {
        foreach (var window in _windows)
        {
            if (window.Contains(point))
                return Rectangle.Intersect(window, monitor.DesktopBounds);
        }
        return monitor.DesktopBounds;
    }

    /// <summary>Called by the overlay the cursor is on, so the others drop their highlight.</summary>
    public void Activate(OverlayForm active)
    {
        foreach (var overlay in _overlays)
        {
            if (overlay != active)
                overlay.ClearHighlight();
        }
    }

    /// <summary>Ends the session with <paramref name="selection"/>; null means cancelled.</summary>
    public void Finish(SelectedArea? selection)
    {
        if (_finished)
            return;
        _finished = true;

        foreach (var overlay in _overlays)
            overlay.Hide();

        // Usually called from an overlay's own input handler; dispose once that handler has returned.
        SynchronizationContext.Current!.Post(_ =>
        {
            Dispose();
            Completed?.Invoke(selection);
        }, null);
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays)
            overlay.Dispose();
        _overlays.Clear();
    }
}
