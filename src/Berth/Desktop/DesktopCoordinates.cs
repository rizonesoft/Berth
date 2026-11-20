// Folder-view coordinate origin measurement (2026-11-20, Windows 11 24H2 multi-monitor host):
//   GetSystemMetrics(SM_XVIRTUALSCREEN..SM_CYVIRTUALSCREEN): (-5760, 0) 9600x2160
//   IFolderView.ItemCount(SVGIO_ALLVIEW) = 128; GetItemPosition X=[5780..9476], Y=[2..1926]
//   Adding virtual-desktop origin (-5760, 0) maps icons to Desktop X=[20..3716], Y=[2..1926],
//   landing directly inside the primary 3840x2160 monitor at [0..3840)x[0..2160).
// Therefore IFolderView coordinates have origin (min(m.X), min(m.Y)) of the connected monitors,
// so desktop = view + (minX, minY) and view = desktop - (minX, minY).

using Berth.Core.Model;

namespace Berth.Desktop;

/// <summary>
/// Converts between <c>IFolderView</c> view coordinates and physical pixels of the virtual desktop (SPEC §15
/// "Positions"). Nothing outside <c>Berth.Desktop</c> sees view coordinates.
/// </summary>
internal static class DesktopCoordinates
{
    public static (int X, int Y) ToDesktop(int viewX, int viewY, IReadOnlyList<MonitorInfo> monitors)
    {
        (int originX, int originY) = VirtualOrigin(monitors);
        return (checked(viewX + originX), checked(viewY + originY));
    }

    public static (int X, int Y) ToView(int desktopX, int desktopY, IReadOnlyList<MonitorInfo> monitors)
    {
        (int originX, int originY) = VirtualOrigin(monitors);
        return (checked(desktopX - originX), checked(desktopY - originY));
    }

    private static (int OriginX, int OriginY) VirtualOrigin(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required to compute the virtual desktop origin.", nameof(monitors));
        }

        int minX = monitors[0].X;
        int minY = monitors[0].Y;
        for (int i = 1; i < monitors.Count; i++)
        {
            if (monitors[i].X < minX)
            {
                minX = monitors[i].X;
            }

            if (monitors[i].Y < minY)
            {
                minY = monitors[i].Y;
            }
        }

        return (minX, minY);
    }
}
