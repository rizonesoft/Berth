using Berth.Core.Model;

namespace Berth.Core;

/// <summary>
/// What makes a stored layout readable (SPEC §10, §14). Loading, finding, saving and deleting all apply this one rule
/// set, so a file Berth would refuse to read is never used, and a layout Berth would refuse is never written.
/// </summary>
internal static class LayoutRules
{
    /// <summary>The longest layout name, after trimming (SPEC §10).</summary>
    public const int MaxNameLength = 40;

    /// <summary>The most monitors a layout may name (SPEC §14): far above any real arrangement, and a bound on the
    /// work of comparing a damaged file with the desktop.</summary>
    public const int MaxMonitors = 64;

    /// <summary>Null when <paramref name="layout"/> is a readable layout stored as <paramref name="fileKey"/>, else why not.</summary>
    public static string? Check(Layout layout, string fileKey)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.SchemaVersion != Layout.CurrentSchemaVersion)
        {
            return $"It has schemaVersion {layout.SchemaVersion}; this version of Berth reads {Layout.CurrentSchemaVersion}.";
        }

        if (layout.Key != fileKey)
        {
            return $"It carries the key {layout.Key} but is stored as {fileKey}.";
        }

        if (layout.Monitors.IsDefaultOrEmpty)
        {
            return "It names no monitor.";
        }

        if (layout.Monitors.Length > MaxMonitors)
        {
            return $"It names {layout.Monitors.Length} monitors, more than the {MaxMonitors} a layout may name.";
        }

        if (layout.Monitors.Any(m => m.Width <= 0 || m.Height <= 0 || m.Scale <= 0))
        {
            return "A monitor has no size or no scale.";
        }

        if (ArrangementKey.From(layout.Monitors) != layout.Key)
        {
            return $"Its key {layout.Key} is not the key of the monitors it names.";
        }

        if (string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Trim() != layout.Name || layout.Name.Length > MaxNameLength)
        {
            return $"Its name is not 1 to {MaxNameLength} characters without surrounding spaces.";
        }

        return IconProblem(layout.Icons, "icons") ?? (layout.Previous is null ? null : IconProblem(layout.Previous.Icons, "previous icons"));
    }

    private static string? IconProblem(System.Collections.Immutable.ImmutableArray<IconPosition> icons, string what)
    {
        if (icons.IsDefault)
        {
            return $"Its {what} are missing.";
        }

        if (icons.Any(i => string.IsNullOrWhiteSpace(i.Id)))
        {
            return $"One of its {what} has no identifier.";
        }

        string? repeated = icons.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
        return repeated is null ? null : $"Two of its {what} are both {repeated}.";
    }
}
