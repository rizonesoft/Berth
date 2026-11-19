using System.Globalization;

namespace Berth.Core;

/// <summary>Why a layout name may not be used (SPEC §10), or <see cref="None"/>.</summary>
public enum NameError
{
    /// <summary>The name may be used.</summary>
    None,

    /// <summary>Nothing is left after trimming: "Enter a name."</summary>
    Empty,

    /// <summary>More than <see cref="LayoutRules.MaxNameLength"/> characters after trimming: "A name can be at most 40 characters."</summary>
    TooLong,

    /// <summary>Another layout has this name, ignoring case: "Another layout already has this name."</summary>
    Taken,
}

/// <summary>
/// The default name of a first save and the naming rules of SPEC §10, in one place for the rename dialog, the save
/// operation and <c>--name</c>. Lengths count as the layout store counts them (<see cref="string.Length"/>), so a
/// name this accepts is one the store keeps.
/// </summary>
public static class LayoutNames
{
    /// <summary>
    /// "1 monitor" or "N monitors", then " (2)", " (3)" and so on until the name is not in <paramref name="taken"/>,
    /// ignoring case (SPEC §15 "Save").
    /// </summary>
    public static string Default(int monitorCount, IEnumerable<string> taken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(monitorCount, 1);
        ArgumentNullException.ThrowIfNull(taken);
        var used = new HashSet<string>(taken.Select(Trimmed), StringComparer.OrdinalIgnoreCase);
        string name = Formats.MonitorCount(monitorCount);
        string candidate = name;
        for (int n = 2; used.Contains(candidate); n++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{name} ({n})");
        }

        return candidate;
    }

    /// <summary>
    /// Whether <paramref name="name"/>, trimmed, may name a layout among <paramref name="others"/> (every other
    /// layout's name, not the one being renamed): 1 to <see cref="LayoutRules.MaxNameLength"/> characters and unique
    /// ignoring case.
    /// </summary>
    public static NameError Validate(string name, IEnumerable<string> others)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(others);
        string trimmed = Trimmed(name);
        if (trimmed.Length == 0)
        {
            return NameError.Empty;
        }

        if (trimmed.Length > LayoutRules.MaxNameLength)
        {
            return NameError.TooLong;
        }

        return others.Any(other => string.Equals(Trimmed(other), trimmed, StringComparison.OrdinalIgnoreCase))
            ? NameError.Taken
            : NameError.None;
    }

    /// <summary>The name as it is stored: without surrounding white space. Every caller stores this.</summary>
    public static string Trimmed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim();
    }
}
