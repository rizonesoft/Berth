using Berth.Core.Model;

namespace Berth.Core;

/// <summary>
/// The desktop's icons, as the rules see them (foundation plan, "Seams"): the shell implementation lives in
/// <c>Berth.Shell</c>, and every rule test uses a fake. No Windows type appears here.
/// </summary>
internal interface IDesktopIcons
{
    /// <summary>Every icon on the desktop with its parsing name, display name and position.</summary>
    /// <exception cref="DesktopUnavailableException">The desktop cannot be reached.</exception>
    IReadOnlyList<IconPosition> ReadIcons();

    /// <summary>
    /// Reads every icon on the desktop together with the exact monitor snapshot used to convert its coordinates, so
    /// callers can detect an A -> B -> A monitor shift that occurs mid-read.
    /// </summary>
    /// <exception cref="DesktopUnavailableException">The desktop cannot be reached.</exception>
    (IReadOnlyList<MonitorInfo> Monitors, IReadOnlyList<IconPosition> Icons) ReadIconsWithMonitors(IMonitors monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return (monitors.Read(), ReadIcons());
    }

    /// <summary>
    /// Moves each icon in <paramref name="icons"/> whose <see cref="IconPosition.Id"/> is on the desktop (parsing
    /// names compared ignoring case) to its position. An icon that is not on the desktop is ignored, and Windows may
    /// adjust a position; callers read the icons again to learn where they landed.
    /// </summary>
    /// <exception cref="DesktopUnavailableException">The desktop cannot be reached.</exception>
    void SetPositions(IReadOnlyList<IconPosition> icons);

    /// <summary>
    /// As <see cref="SetPositions(IReadOnlyList{IconPosition})"/>, running <paramref name="beforeMove"/> only after
    /// all matched positions have been validated and converted, immediately before any icon is moved.
    /// </summary>
    /// <exception cref="DesktopUnavailableException">The desktop cannot be reached.</exception>
    void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove);

    /// <summary>Whether the desktop arranges its icons automatically.</summary>
    /// <exception cref="DesktopUnavailableException">The desktop cannot be reached.</exception>
    bool AutoArrange { get; }

    /// <summary>
    /// Turns auto arrange off. False when it could not be turned off: Windows kept it on, or the desktop's folder view
    /// does not offer the setting; read <see cref="AutoArrange"/> to learn its state.
    /// </summary>
    /// <exception cref="DesktopUnavailableException">The desktop cannot be reached.</exception>
    bool TryTurnAutoArrangeOff();
}
