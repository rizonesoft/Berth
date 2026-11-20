using System.IO;
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
    private readonly LayoutStore? _store;
    private readonly BerthPaths? _paths;
    private readonly IMonitors? _monitors;
    private readonly Log? _log;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly TimeZoneInfo _zone;

    public CliRunner(LayoutService service, TextWriter output, TextWriter error)
        : this(service, store: null, paths: null, monitors: null, log: null, output, error, TimeZoneInfo.Local)
    {
    }

    public CliRunner(
        LayoutService service,
        LayoutStore? store,
        BerthPaths? paths,
        IMonitors? monitors,
        Log? log,
        TextWriter output,
        TextWriter error,
        TimeZoneInfo zone)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _store = store;
        _paths = paths;
        _monitors = monitors;
        _log = log;
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _zone = zone ?? throw new ArgumentNullException(nameof(zone));
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
            CliCommand.List => RunList(
                _store ?? throw new InvalidOperationException("LayoutStore is required for list."),
                _paths ?? throw new InvalidOperationException("BerthPaths is required for list."),
                _monitors ?? throw new InvalidOperationException("IMonitors is required for list."),
                _log,
                _output,
                _error,
                _zone),
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
            log?.Fail(line, ex);
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
            if (displayName.Length > maxNameWidth)
            {
                maxNameWidth = displayName.Length;
            }

            bool isActive = activeKey is not null && string.Equals(layout.Key, activeKey, StringComparison.Ordinal);
            rows.Add(new ListRow(layout, displayName, isActive));
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
            string paddedName = row.DisplayName.PadRight(maxNameWidth);
            string monitorCount = Formats.MonitorCount(row.Layout.Monitors.Length);
            string iconCount = Formats.IconCount(row.Layout.Icons.Length);
            string savedAt = Formats.ListDate(row.Layout.SavedAt, zone);
            output.WriteLine($"{marker}{paddedName}  {monitorCount}  {iconCount}  {savedAt}");
        }

        return CliExit.Done;
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

    private readonly record struct ListRow(Layout Layout, string DisplayName, bool IsActive);
}
