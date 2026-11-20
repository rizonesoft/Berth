using System.Globalization;
using System.IO;
using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Cli;

/// <summary>Exit codes of the command-line interface (SPEC §13).</summary>
internal static class CliExit
{
    public const int Done = 0;
    public const int Error = 1;
    public const int Usage = 2;
    public const int Refused = 3;
}

/// <summary>Runs parsed CLI commands against Core services and writes single-line results (SPEC §13).</summary>
internal sealed class CliRunner
{
    private readonly LayoutService _service;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CliRunner(LayoutService service, TextWriter output, TextWriter error)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public int Run(CliArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Error is not null)
        {
            return CliExit.Usage;
        }

        return args.Command switch
        {
            CliCommand.Save => WriteOperationResult(_service.Save(args.Name)),
            CliCommand.Restore => WriteOperationResult(_service.Restore()),
            CliCommand.Undo => WriteOperationResult(_service.Undo()),
            _ => CliExit.Usage,
        };
    }

    /// <summary>
    /// Lists saved layouts without touching desktop icons or acquiring <see cref="OperationLock"/> (SPEC §13).
    /// </summary>
    public static int RunList(
        LayoutStore store,
        BerthPaths paths,
        IMonitors monitors,
        Log? log,
        TextWriter output,
        TextWriter error,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(zone);

        LayoutSet loaded;
        try
        {
            loaded = store.LoadAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string line = Messages.ReadFailed(paths.LayoutsFolder, ex.Message);
            log?.Fail(line);
            error.WriteLine(line);
            return CliExit.Error;
        }

        foreach (UnreadableFile unreadable in loaded.Unreadable)
        {
            error.WriteLine(Messages.UnreadableLayout(unreadable.FileName));
        }

        if (loaded.Layouts.Count == 0)
        {
            output.WriteLine("No layouts saved yet.");
            return CliExit.Done;
        }

        string? activeKey = TryReadActiveKey(monitors);
        List<ListRow> rows = new(loaded.Layouts.Count);
        int maxNameWidth = 0;

        foreach (Layout layout in loaded.Layouts)
        {
            string displayName = Messages.SingleLine(layout.Name);
            int width = DisplayWidth(displayName);
            if (width > maxNameWidth)
            {
                maxNameWidth = width;
            }

            bool isActive = activeKey is not null && string.Equals(layout.Key, activeKey, StringComparison.Ordinal);
            rows.Add(new ListRow(layout, displayName, width, isActive));
        }

        rows.Sort(static (a, b) =>
        {
            int activeCmp = b.IsActive.CompareTo(a.IsActive);
            if (activeCmp != 0)
            {
                return activeCmp;
            }

            int ignoreCaseCmp = StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);
            if (ignoreCaseCmp != 0)
            {
                return ignoreCaseCmp;
            }

            int ordinalCmp = StringComparer.Ordinal.Compare(a.DisplayName, b.DisplayName);
            if (ordinalCmp != 0)
            {
                return ordinalCmp;
            }

            return StringComparer.Ordinal.Compare(a.Layout.Key, b.Layout.Key);
        });

        foreach (ListRow row in rows)
        {
            string marker = row.IsActive ? "* " : "  ";
            string padding = new(' ', maxNameWidth - row.DisplayWidth);
            string monitorCount = Formats.MonitorCount(row.Layout.Monitors.Length);
            string iconCount = Formats.IconCount(row.Layout.Icons.Length);
            string savedAt = Formats.ListDate(row.Layout.SavedAt, zone);
            output.WriteLine($"{marker}{row.DisplayName}{padding}  {monitorCount}  {iconCount}  {savedAt}");
        }

        return CliExit.Done;
    }

    internal static int DisplayWidth(string text)
    {
        int width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            width += RuneDisplayWidth(rune);
        }

        return width;
    }

    private static int RuneDisplayWidth(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
        {
            return 0;
        }

        int v = rune.Value;
        bool wide =
            v >= 0x1100 &&
            (v <= 0x115F
             || v is 0x2329 or 0x232A
             || (v >= 0x2E80 && v <= 0xA4CF && v != 0x303F)
             || (v >= 0xAC00 && v <= 0xD7A3)
             || (v >= 0xF900 && v <= 0xFAFF)
             || (v >= 0xFE10 && v <= 0xFE19)
             || (v >= 0xFE30 && v <= 0xFE6F)
             || (v >= 0xFF00 && v <= 0xFF60)
             || (v >= 0xFFE0 && v <= 0xFFE6)
             || (v >= 0x1F300 && v <= 0x1FAFF)
             || (v >= 0x20000 && v <= 0x3FFFD));

        return wide ? 2 : 1;
    }

    private static string? TryReadActiveKey(IMonitors monitors)
    {
        try
        {
            return ArrangementKey.From(monitors.Read());
        }
        catch (DesktopUnavailableException)
        {
            return null;
        }
    }

    private int WriteOperationResult(OperationResult result)
    {
        string line = Messages.Cli(result);
        switch (result.Kind)
        {
            case OperationKind.Saved:
            case OperationKind.Restored:
            case OperationKind.Undone:
                _output.WriteLine(line);
                return CliExit.Done;

            case OperationKind.Refused when result.Refusal == Refusal.InvalidName:
                _error.WriteLine(line);
                return CliExit.Usage;

            case OperationKind.Refused:
                _error.WriteLine(line);
                return CliExit.Refused;

            case OperationKind.Failed:
                _error.WriteLine(line);
                return CliExit.Error;

            default:
                throw new InvalidOperationException($"Unexpected CLI operation result kind '{result.Kind}'.");
        }
    }

    private readonly record struct ListRow(Layout Layout, string DisplayName, int DisplayWidth, bool IsActive);
}
