using Berth.Core;
using Berth.Core.Model;

namespace Berth.Window;

/// <summary>One entry in the layouts pane (SPEC §5): either a saved layout or the unsaved "This arrangement" row.</summary>
internal sealed record LayoutRow(
    string Key,
    string Name,
    string Subtitle,
    bool IsActive,
    bool IsUnsaved,
    Layout? Layout)
{
    public const string UnsavedTitle = "This arrangement";
    public const string UnsavedSubtitle = "Not saved yet";

    public static LayoutRow FromSaved(Layout layout, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string subtitle = $"{Formats.MonitorCount(layout.Monitors.Length)} \u00B7 {Formats.IconCount(layout.Icons.Length)}";
        return new LayoutRow(layout.Key, layout.Name, subtitle, isActive, IsUnsaved: false, layout);
    }

    public static LayoutRow Unsaved(string arrangementKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arrangementKey);
        return new LayoutRow(arrangementKey, UnsavedTitle, UnsavedSubtitle, IsActive: false, IsUnsaved: true, Layout: null);
    }
}
