using System.Windows;
using Berth.Core.Model;

namespace Berth.Window;

/// <summary>
/// Converts window rectangles between WPF device-independent units (DIPs) and physical pixels of the virtual desktop
/// (SPEC §14), using the target monitor's DPI scale.
/// </summary>
internal static class WindowPixels
{
    public static WindowPlacement ToPhysical(Rect dips, double scale)
    {
        ValidateScale(scale);
        return new WindowPlacement(
            (int)Math.Round(dips.X * scale),
            (int)Math.Round(dips.Y * scale),
            (int)Math.Round(dips.Width * scale),
            (int)Math.Round(dips.Height * scale));
    }

    public static Rect ToDips(WindowPlacement physical, double scale)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ValidateScale(scale);
        return new Rect(
            physical.X / scale,
            physical.Y / scale,
            physical.Width / scale,
            physical.Height / scale);
    }

    /// <summary>
    /// Picks the DPI scale (<c>MonitorInfo.Scale / 100.0</c>) of the monitor with the largest intersection area with
    /// <paramref name="physical"/>, falling back to the primary monitor, the first monitor, or <c>1.0</c>.
    /// </summary>
    public static double ScaleFor(WindowPlacement? physical, IReadOnlyList<MonitorInfo>? monitors)
    {
        if (monitors is null || monitors.Count == 0)
        {
            return 1.0;
        }

        MonitorInfo? chosen = null;
        if (physical is not null && physical.Width > 0 && physical.Height > 0)
        {
            long bestArea = 0;
            long pLeft = physical.X;
            long pTop = physical.Y;
            long pRight = pLeft + physical.Width;
            long pBottom = pTop + physical.Height;

            foreach (MonitorInfo monitor in monitors)
            {
                long mLeft = monitor.X;
                long mTop = monitor.Y;
                long mRight = mLeft + monitor.Width;
                long mBottom = mTop + monitor.Height;

                long overlapW = Math.Min(pRight, mRight) - Math.Max(pLeft, mLeft);
                long overlapH = Math.Min(pBottom, mBottom) - Math.Max(pTop, mTop);
                if (overlapW > 0 && overlapH > 0)
                {
                    long area = overlapW * overlapH;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        chosen = monitor;
                    }
                }
            }
        }

        chosen ??= monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        return chosen.Scale > 0 ? chosen.Scale / 100.0 : 1.0;
    }

    private static void ValidateScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "DPI scale must be a finite positive number.");
        }
    }
}
