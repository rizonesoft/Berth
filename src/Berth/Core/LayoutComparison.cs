using Berth.Core.Model;

namespace Berth.Core;

/// <summary>How a plotted icon stands against the desktop (SPEC §7 table).</summary>
internal enum IconState
{
    /// <summary>On the desktop at exactly its saved position (SPEC §15 "In place").</summary>
    InPlace,

    /// <summary>On the desktop somewhere else.</summary>
    Moved,

    /// <summary>Not on the desktop.</summary>
    Missing,

    /// <summary>Not compared: another arrangement's layout, the unsaved arrangement, or an unreachable desktop.</summary>
    Neutral,
}

/// <summary>
/// One plotted icon: <paramref name="Saved"/> is the position plotted (the saved one, or the live one for the unsaved
/// arrangement), <paramref name="Current"/> where it is on the desktop now when it is there and compared.
/// </summary>
internal sealed record ComparedIcon(IconPosition Saved, IconState State, IconPosition? Current, int MonitorNumber, bool OffScreen);

/// <summary>What the position map and the status line show (SPEC §7, §8): the icons in reading order and the counts.</summary>
internal sealed record LayoutComparison(IReadOnlyList<ComparedIcon> Icons, int InPlace, int Moved, int Missing)
{
    /// <summary>
    /// Each icon of <paramref name="layout"/> against <paramref name="desktop"/>, matched by parsing name ignoring case
    /// (SPEC §15 "Matching"), never by display name. Icons only on the desktop are not part of the layout's map.
    /// </summary>
    public static LayoutComparison Compare(Layout layout, IReadOnlyList<IconPosition> desktop)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(desktop);
        var live = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (IconPosition icon in desktop)
        {
            live.TryAdd(icon.Id, icon);
        }

        IReadOnlyList<MonitorInfo> ordered = MonitorNumbering.Order(layout.Monitors);
        var icons = layout.Icons.Select(saved =>
        {
            IconPosition? now = live.GetValueOrDefault(saved.Id);
            IconState state = now is null ? IconState.Missing
                : now.X == saved.X && now.Y == saved.Y ? IconState.InPlace
                : IconState.Moved;
            return Plot(saved, state, now, ordered);
        });
        return Of(icons);
    }

    /// <summary>The layout's icons at their saved positions, every one <see cref="IconState.Neutral"/>.</summary>
    public static LayoutComparison Neutral(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        IReadOnlyList<MonitorInfo> ordered = MonitorNumbering.Order(layout.Monitors);
        return Of(layout.Icons.Select(saved => Plot(saved, IconState.Neutral, null, ordered)));
    }

    /// <summary>The desktop's icons where they are now, on <paramref name="monitors"/>, every one <see cref="IconState.Neutral"/>.</summary>
    public static LayoutComparison Unsaved(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<IconPosition> desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        IReadOnlyList<MonitorInfo> ordered = MonitorNumbering.Order(monitors);
        return Of(desktop.Select(icon => Plot(icon, IconState.Neutral, null, ordered)));
    }

    private static ComparedIcon Plot(IconPosition saved, IconState state, IconPosition? current, IReadOnlyList<MonitorInfo> ordered)
    {
        (int number, bool offScreen) = MonitorNumbering.Locate(ordered, saved.X, saved.Y);
        return new ComparedIcon(saved, state, current, number, offScreen);
    }

    /// <summary>The icons in reading order (SPEC §7: top to bottom, then left to right, by the plotted position) and their counts.</summary>
    private static LayoutComparison Of(IEnumerable<ComparedIcon> icons)
    {
        ComparedIcon[] ordered = [.. icons.OrderBy(i => i.Saved.Y).ThenBy(i => i.Saved.X)];
        return new LayoutComparison(
            ordered,
            ordered.Count(i => i.State == IconState.InPlace),
            ordered.Count(i => i.State == IconState.Moved),
            ordered.Count(i => i.State == IconState.Missing));
    }
}
