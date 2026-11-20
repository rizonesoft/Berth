using Berth.Core.Model;

namespace Berth.Window;

/// <summary>
/// Pure geometry rules deciding whether a saved window placement is visible and large enough for the connected
/// monitors, and computing the primary-monitor centred fallback when it is not (SPEC §4, §14).
/// </summary>
internal static class WindowPlacementRule
{
    public const int MinimumVisibleOverlapPx = 100;

    /// <summary>
    /// Returns <paramref name="saved"/> when its size is at least <paramref name="minWidth"/> by
    /// <paramref name="minHeight"/> physical pixels and at least 100 by 100 px of it lies on some monitor in
    /// <paramref name="monitors"/>; otherwise returns <see langword="null"/>.
    /// </summary>
    public static WindowPlacement? Resolve(
        WindowPlacement? saved,
        IReadOnlyList<MonitorInfo> monitors,
        int minWidth,
        int minHeight)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (saved is null || monitors.Count == 0)
        {
            return null;
        }

        if (saved.Width <= 0 || saved.Height <= 0 || saved.Width < minWidth || saved.Height < minHeight)
        {
            return null;
        }

        long sLeft = saved.X;
        long sTop = saved.Y;
        long sRight = sLeft + saved.Width;
        long sBottom = sTop + saved.Height;

        foreach (MonitorInfo monitor in monitors)
        {
            long mLeft = monitor.X;
            long mTop = monitor.Y;
            long mRight = mLeft + monitor.Width;
            long mBottom = mTop + monitor.Height;

            long overlapW = Math.Min(sRight, mRight) - Math.Max(sLeft, mLeft);
            long overlapH = Math.Min(sBottom, mBottom) - Math.Max(sTop, mTop);
            if (overlapW >= MinimumVisibleOverlapPx && overlapH >= MinimumVisibleOverlapPx)
            {
                return saved;
            }
        }

        return null;
    }

    /// <summary>
    /// Converts DIP minimums to physical pixels at the target monitor's scale (selected from <paramref name="saved"/>
    /// first) and delegates to <see cref="Resolve"/>.
    /// </summary>
    public static WindowPlacement? ResolveForDipsMinimum(
        WindowPlacement? saved,
        IReadOnlyList<MonitorInfo> monitors,
        double minWidthDips,
        double minHeightDips)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (saved is null || monitors.Count == 0)
        {
            return null;
        }

        double scale = WindowPixels.ScaleFor(saved, monitors);
        int minWidthPx = (int)Math.Round(minWidthDips * scale);
        int minHeightPx = (int)Math.Round(minHeightDips * scale);
        return Resolve(saved, monitors, minWidthPx, minHeightPx);
    }

    /// <summary>
    /// Computes the default centred placement in physical pixels on the primary monitor's work area (falling back to
    /// the primary <see cref="MonitorInfo"/> bounds when <paramref name="primaryWorkArea"/> is unavailable).
    /// </summary>
    public static WindowPlacement? CenterOnPrimary(
        IReadOnlyList<MonitorInfo>? monitors,
        WindowPlacement? primaryWorkArea,
        double defaultWidthDips,
        double defaultHeightDips)
    {
        MonitorInfo? primary = monitors is { Count: > 0 }
            ? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0]
            : null;

        WindowPlacement? work = primaryWorkArea
            ?? (primary is not null ? new WindowPlacement(primary.X, primary.Y, primary.Width, primary.Height) : null);

        if (work is null || work.Width <= 0 || work.Height <= 0)
        {
            return null;
        }

        double scale = primary is not null && primary.Scale > 0 ? primary.Scale / 100.0 : 1.0;
        int width = Math.Max(1, (int)Math.Round(defaultWidthDips * scale));
        int height = Math.Max(1, (int)Math.Round(defaultHeightDips * scale));

        int x = work.X + Math.Max(0, (work.Width - width) / 2);
        int y = work.Y + Math.Max(0, (work.Height - height) / 2);
        return new WindowPlacement(x, y, width, height);
    }
}
