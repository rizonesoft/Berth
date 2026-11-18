using Berth.Core.Model;

namespace Berth.Core;

/// <summary>Monitor numbers as the position map labels them (SPEC §7): left to right, then top to bottom, from 1.</summary>
internal static class MonitorNumbering
{
    /// <summary>The monitors in numbering order: by <see cref="MonitorInfo.X"/>, then <see cref="MonitorInfo.Y"/>.</summary>
    public static IReadOnlyList<MonitorInfo> Order(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return [.. monitors.OrderBy(m => m.X).ThenBy(m => m.Y)];
    }

    /// <summary>
    /// The number of the monitor that holds (<paramref name="x"/>, <paramref name="y"/>), with off-screen false; for a
    /// point outside every monitor, the number of the nearest one by distance to its rectangle, with off-screen true.
    /// A monitor holds <c>X</c> to <c>X + Width - 1</c> and <c>Y</c> to <c>Y + Height - 1</c>, so a shared edge belongs
    /// to the monitor on its right or below.
    /// </summary>
    public static (int Number, bool OffScreen) Locate(IReadOnlyList<MonitorInfo> ordered, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentOutOfRangeException.ThrowIfZero(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            MonitorInfo m = ordered[i];
            if (x >= m.X && x < m.X + m.Width && y >= m.Y && y < m.Y + m.Height)
            {
                return (i + 1, false);
            }
        }

        int nearest = 0;
        long best = long.MaxValue;
        for (int i = 0; i < ordered.Count; i++)
        {
            MonitorInfo m = ordered[i];
            long dx = Math.Max(Math.Max((long)m.X - x, 0), (long)x - (m.X + m.Width - 1));
            long dy = Math.Max(Math.Max((long)m.Y - y, 0), (long)y - (m.Y + m.Height - 1));
            long distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return (nearest + 1, true);
    }
}
