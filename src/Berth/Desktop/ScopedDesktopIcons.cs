using Berth.Core;
using Berth.Core.Model;

namespace Berth.Desktop;

/// <summary>
/// Wraps an <see cref="IDesktopIcons"/> so reads and writes only ever see or move icons whose live display name
/// starts with a fixed prefix (ordinal, ignoring case), and <see cref="TryTurnAutoArrangeOff"/> never touches the
/// desktop.
/// </summary>
internal sealed class ScopedDesktopIcons : IDesktopIcons
{
    private readonly IDesktopIcons _inner;
    private readonly string _prefix;

    public ScopedDesktopIcons(IDesktopIcons inner, string prefix)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _inner = inner;
        _prefix = prefix;
    }

    public bool AutoArrange => _inner.AutoArrange;

    public IReadOnlyList<IconPosition> ReadIcons()
    {
        if (_inner is ShellDesktopIcons shell)
        {
            return shell.ReadIcons(_prefix);
        }

        IReadOnlyList<IconPosition> all = _inner.ReadIcons();
        var scoped = new List<IconPosition>();
        foreach (IconPosition icon in all)
        {
            if (MatchesPrefix(icon.Name))
            {
                scoped.Add(icon);
            }
        }

        return scoped;
    }

    public void SetPositions(IReadOnlyList<IconPosition> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        if (_inner is ShellDesktopIcons shell)
        {
            shell.SetPositions(icons, beforeMove: null, _prefix);
            return;
        }

        IReadOnlyList<IconPosition> filtered = FilterWritable(icons);
        _inner.SetPositions(filtered);
    }

    public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(beforeMove);
        if (_inner is ShellDesktopIcons shell)
        {
            shell.SetPositions(icons, beforeMove, _prefix);
            return;
        }

        IReadOnlyList<IconPosition> filtered = FilterWritable(icons);
        _inner.SetPositions(filtered, beforeMove);
    }

    public bool TryTurnAutoArrangeOff() => false;

    private List<IconPosition> FilterWritable(IReadOnlyList<IconPosition> icons)
    {
        if (icons.Count == 0)
        {
            return [];
        }

        var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IconPosition live in _inner.ReadIcons())
        {
            if (MatchesPrefix(live.Name))
            {
                allowedIds.Add(live.Id);
            }
        }

        if (allowedIds.Count == 0)
        {
            return [];
        }

        var filtered = new List<IconPosition>(icons.Count);
        foreach (IconPosition requested in icons)
        {
            if (allowedIds.Contains(requested.Id))
            {
                filtered.Add(requested);
            }
        }

        return filtered;
    }

    private bool MatchesPrefix(string name) =>
        name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
}
