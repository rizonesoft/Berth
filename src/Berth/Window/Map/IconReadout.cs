using System.Globalization;
using Berth.Core;

namespace Berth.Window.Map;

internal readonly record struct ReadoutLines(string Primary, string Secondary, bool HasSelection);

/// <summary>
/// Pure formatter for the position map's bottom-left icon readout, bottom-right count, and per-marker assistive
/// technology name (SPEC §7, §9).
/// </summary>
internal static class IconReadout
{
    public const string EmptyPrompt = "Select an icon to see its position.";

    public static ReadoutLines For(ComparedIcon? icon)
    {
        if (icon is null)
        {
            return new ReadoutLines(EmptyPrompt, string.Empty, HasSelection: false);
        }

        string coords = string.Create(
            CultureInfo.InvariantCulture,
            $"x {icon.Saved.X} \u00B7 y {icon.Saved.Y} \u00B7 monitor {icon.MonitorNumber}");

        string stateWord = StateWord(icon.State);
        if (!string.IsNullOrEmpty(stateWord))
        {
            coords = $"{coords} \u00B7 {stateWord}";
        }

        if (icon.OffScreen)
        {
            coords = $"{coords} \u00B7 off screen";
        }

        return new ReadoutLines(icon.Saved.Name, coords, HasSelection: true);
    }

    public static string StateWord(IconState state) => state switch
    {
        IconState.InPlace => "in place",
        IconState.Moved => "moved",
        IconState.Missing => "not on the desktop",
        IconState.Neutral => string.Empty,
        _ => string.Empty,
    };

    public static string CountText(LayoutComparison? comparison, ComparisonKind kind)
    {
        if (comparison is null)
        {
            return string.Empty;
        }

        if (kind == ComparisonKind.Compared)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{comparison.InPlace} of {comparison.Icons.Count} in place");
        }

        return Formats.IconCount(comparison.Icons.Count);
    }

    public static string AutomationName(ComparedIcon icon, bool? offScreenOverride = null)
    {
        ArgumentNullException.ThrowIfNull(icon);
        string text = string.Create(
            CultureInfo.InvariantCulture,
            $"{icon.Saved.Name}, x {icon.Saved.X}, y {icon.Saved.Y}, monitor {icon.MonitorNumber}");

        string stateWord = StateWord(icon.State);
        if (!string.IsNullOrEmpty(stateWord))
        {
            text = $"{text}, {stateWord}";
        }

        if (offScreenOverride ?? icon.OffScreen)
        {
            text = $"{text}, off screen";
        }

        return text;
    }
}
