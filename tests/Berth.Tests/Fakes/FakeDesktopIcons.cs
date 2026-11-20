using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Fakes;

/// <summary>
/// A desktop for rule tests: it moves only icons that are on it (matched by parsing name ignoring case, never by
/// display name), records every <see cref="IDesktopIcons.SetPositions"/> call, and can imitate Windows adjusting positions, keeping
/// auto arrange on, or being unreachable.
/// </summary>
internal sealed class FakeDesktopIcons : IDesktopIcons
{
    private readonly List<IconPosition> icons;
    private bool autoArrange;

    public FakeDesktopIcons(params IconPosition[] icons)
    {
        this.icons = [.. icons];
    }

    /// <summary>Every <see cref="IDesktopIcons.SetPositions"/> call, with a copy of the icons it asked for, recorded before anything
    /// moves and before an unavailable desktop refuses it, so a test can prove a write was never attempted.</summary>
    public List<IReadOnlyList<IconPosition>> Calls { get; } = [];

    /// <summary>Called at the start of each <see cref="IDesktopIcons.SetPositions"/> call, before anything moves.</summary>
    public Action? BeforeSet { get; set; }

    /// <summary>When true, <see cref="IDesktopIcons.SetPositions"/> throws as Explorer would while restarting.</summary>
    public bool UnavailableOnSet { get; set; }

    /// <summary>How many times the icons were read.</summary>
    public int Reads { get; private set; }

    /// <summary>When true, every member throws <see cref="DesktopUnavailableException"/> and changes nothing.</summary>
    public bool Unavailable { get; set; }

    /// <summary>When true, <see cref="IDesktopIcons.TryTurnAutoArrangeOff"/> returns false and auto arrange stays on.</summary>
    public bool KeepsAutoArrange { get; set; }

    /// <summary>How far Windows shifts a set position, in both directions (0: it lands exactly).</summary>
    public int SnapBy { get; set; }

    /// <summary>The parsing names <see cref="SnapBy"/> applies to (ignoring case); null for every icon.</summary>
    public IReadOnlySet<string>? SnapIds { get; set; }

    /// <summary>Sets auto arrange directly, as the owner would in Explorer.</summary>
    public void SetAutoArrange(bool on) => autoArrange = on;

    bool IDesktopIcons.AutoArrange
    {
        get
        {
            ThrowIfUnavailable();
            return autoArrange;
        }
    }

    IReadOnlyList<IconPosition> IDesktopIcons.ReadIcons()
    {
        Reads++;
        ThrowIfUnavailable();
        return [.. icons];
    }

    void IDesktopIcons.SetPositions(IReadOnlyList<IconPosition> requested) =>
        SetPositionsCore(requested, beforeMove: null);

    void IDesktopIcons.SetPositions(IReadOnlyList<IconPosition> requested, Action beforeMove)
    {
        ArgumentNullException.ThrowIfNull(beforeMove);
        SetPositionsCore(requested, beforeMove);
    }

    private void SetPositionsCore(IReadOnlyList<IconPosition> requested, Action? beforeMove)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (beforeMove is null)
        {
            Calls.Add([.. requested]);
            ThrowIfUnavailable();
        }
        else
        {
            ThrowIfUnavailable();
            beforeMove();
            Calls.Add([.. requested]);
        }

        BeforeSet?.Invoke();
        if (UnavailableOnSet)
        {
            throw new DesktopUnavailableException("Explorer restarted.");
        }
        foreach (IconPosition wanted in requested)
        {
            int index = icons.FindIndex(icon => string.Equals(icon.Id, wanted.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            bool snaps = SnapIds is null || SnapIds.Contains(icons[index].Id, StringComparer.OrdinalIgnoreCase);
            int shift = snaps ? SnapBy : 0;
            icons[index] = icons[index] with { X = wanted.X + shift, Y = wanted.Y + shift };
        }
    }

    bool IDesktopIcons.TryTurnAutoArrangeOff()
    {
        ThrowIfUnavailable();
        if (KeepsAutoArrange)
        {
            return false;
        }

        autoArrange = false;
        return true;
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new DesktopUnavailableException("The fake desktop is unavailable.", new InvalidOperationException("Explorer is not running."));
        }
    }
}
