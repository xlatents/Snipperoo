using System.Drawing;
using Vortice.DXGI;

namespace Snipperoo.Capture;

/// <summary>
/// One monitor as ddagrab sees it. <paramref name="QuarterTurns"/> is how many clockwise quarter turns take
/// the upright desktop image to the framebuffer that Desktop Duplication returns (0 when not rotated).
/// </summary>
internal sealed record MonitorOutput(
    string DeviceName,
    Rectangle DesktopBounds,
    int AdapterIndex,
    int OutputIndex,
    int QuarterTurns)
{
    /// <summary>Lists all monitors attached to the desktop, with the adapter/output indices ddagrab needs.</summary>
    public static IReadOnlyList<MonitorOutput> Enumerate()
    {
        var outputs = new List<MonitorOutput>();
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                {
                    using (output)
                    {
                        OutputDescription desc = output!.Description;
                        if (!desc.AttachedToDesktop)
                            continue;
                        var r = desc.DesktopCoordinates;
                        outputs.Add(new MonitorOutput(
                            desc.DeviceName,
                            Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom),
                            (int)a,
                            (int)o,
                            ToQuarterTurns(desc.Rotation)));
                    }
                }
            }
        }

        return outputs;
    }

    // The framebuffer is the desktop turned counter-clockwise by the DXGI angle (ROTATE270 = one turn clockwise).
    private static int ToQuarterTurns(ModeRotation rotation) => rotation switch
    {
        ModeRotation.Rotate90 => 3,
        ModeRotation.Rotate180 => 2,
        ModeRotation.Rotate270 => 1,
        _ => 0,
    };

}
