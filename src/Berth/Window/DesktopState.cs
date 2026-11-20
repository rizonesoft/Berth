using Berth.Core.Model;

namespace Berth.Window;

/// <summary>Snapshot of the live desktop captured by <see cref="MainViewModel.RefreshAsync"/>.</summary>
internal sealed record DesktopState(
    IReadOnlyList<IconPosition> Icons,
    IReadOnlyList<MonitorInfo> Monitors,
    bool AutoArrange,
    bool Unavailable,
    DateTimeOffset CheckedAt)
{
    public static DesktopState Empty(DateTimeOffset checkedAt) =>
        new([], [], AutoArrange: false, Unavailable: false, checkedAt);
}

/// <summary>Which comparison mode produced <see cref="MainViewModel.Comparison"/>.</summary>
internal enum ComparisonKind
{
    None,
    Compared,
    Neutral,
    Unsaved,
}
