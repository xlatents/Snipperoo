using System.Drawing;
using Snipperoo.Capture;

namespace Snipperoo.Selection;

internal enum SelectionPurpose { Record, Screenshot }

/// <summary>
/// What the user picked: a rectangle in desktop coordinates on one monitor. For screenshots, <paramref name="Image"/>
/// is that area of the frozen overlay screenshot (exactly what the user saw); the receiver owns and disposes it.
/// </summary>
internal sealed record SelectedArea(MonitorOutput Monitor, Rectangle Bounds, Bitmap? Image);
