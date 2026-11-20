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
        if (TryRunStandalone(args, _output, _error, out int standaloneExit))
        {
            return standaloneExit;
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
    /// Handles commands that require no storage, desktop, or tray state (<c>--help</c>, <c>--version</c>, and parser
    /// usage errors) through one shared path used by both <see cref="Run"/> and <c>Program.Run</c> (SPEC §13, §17).
    /// </summary>
    public static bool TryRunStandalone(CliArguments args, TextWriter output, TextWriter error, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Error is not null)
        {
            error.WriteLine($"berth: {args.Error}");
            error.WriteLine("Run \"berth --help\" for the commands.");
            exitCode = CliExit.Usage;
            return true;
        }

        if (args.Command == CliCommand.Help)
        {
            output.WriteLine(Usage.Text);
            exitCode = CliExit.Done;
            return true;
        }

        if (args.Command == CliCommand.Version)
        {
            output.WriteLine("Berth " + AppInfo.Version);
            exitCode = CliExit.Done;
            return true;
        }

        exitCode = 0;
        return false;
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

    private static readonly (int Start, int End)[] EastAsianWideAndFullwidthRanges =
    [
        // Complete Unicode 16.0.0 EastAsianWidth (W and F) ranges.
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC),
        (0x23F0, 0x23F0), (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615),
        (0x2630, 0x2637), (0x2648, 0x2653), (0x267F, 0x267F), (0x268A, 0x268F),
        (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE),
        (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA),
        (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD),
        (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C),
        (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50),
        (0x2B55, 0x2B55), (0x2E80, 0x2E99), (0x2E9B, 0x2EF3), (0x2F00, 0x2FD5),
        (0x2FF0, 0x303E), (0x3041, 0x3096), (0x3099, 0x30FF), (0x3105, 0x312F),
        (0x3131, 0x318E), (0x3190, 0x31E5), (0x31EF, 0x321E), (0x3220, 0x3247),
        (0x3250, 0xA48C), (0xA490, 0xA4C6), (0xA960, 0xA97C), (0xAC00, 0xD7A3),
        (0xF900, 0xFAFF), (0xFE10, 0xFE19), (0xFE30, 0xFE52), (0xFE54, 0xFE66),
        (0xFE68, 0xFE6B), (0xFF01, 0xFF60), (0xFFE0, 0xFFE6), (0x16FE0, 0x16FE4),
        (0x16FF0, 0x16FF1), (0x17000, 0x187F7), (0x18800, 0x18CD5), (0x18CFF, 0x18D08),
        (0x1AFF0, 0x1AFF3), (0x1AFF5, 0x1AFFB), (0x1AFFD, 0x1AFFE), (0x1B000, 0x1B122),
        (0x1B132, 0x1B132), (0x1B150, 0x1B152), (0x1B155, 0x1B155), (0x1B164, 0x1B167),
        (0x1B170, 0x1B2FB), (0x1D300, 0x1D356), (0x1D360, 0x1D376), (0x1F004, 0x1F004),
        (0x1F0CF, 0x1F0CF), (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F200, 0x1F202),
        (0x1F210, 0x1F23B), (0x1F240, 0x1F248), (0x1F250, 0x1F251), (0x1F260, 0x1F265),
        (0x1F300, 0x1F320), (0x1F32D, 0x1F335), (0x1F337, 0x1F37C), (0x1F37E, 0x1F393),
        (0x1F3A0, 0x1F3CA), (0x1F3CF, 0x1F3D3), (0x1F3E0, 0x1F3F0), (0x1F3F4, 0x1F3F4),
        (0x1F3F8, 0x1F43E), (0x1F440, 0x1F440), (0x1F442, 0x1F4FC), (0x1F4FF, 0x1F53D),
        (0x1F54B, 0x1F54E), (0x1F550, 0x1F567), (0x1F57A, 0x1F57A), (0x1F595, 0x1F596),
        (0x1F5A4, 0x1F5A4), (0x1F5FB, 0x1F64F), (0x1F680, 0x1F6C5), (0x1F6CC, 0x1F6CC),
        (0x1F6D0, 0x1F6D2), (0x1F6D5, 0x1F6D7), (0x1F6DC, 0x1F6DF), (0x1F6EB, 0x1F6EC),
        (0x1F6F4, 0x1F6FC), (0x1F7E0, 0x1F7EB), (0x1F7F0, 0x1F7F0), (0x1F90C, 0x1F93A),
        (0x1F93C, 0x1F945), (0x1F947, 0x1F9FF), (0x1FA70, 0x1FA7C), (0x1FA80, 0x1FA89),
        (0x1FA8F, 0x1FAC6), (0x1FACE, 0x1FADC), (0x1FADF, 0x1FAE9), (0x1FAF0, 0x1FAF8),
        (0x20000, 0x2FFFD), (0x30000, 0x3FFFD),
    ];

    private static int RuneDisplayWidth(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
        {
            return 0;
        }

        return IsEastAsianWideOrFullwidth(rune.Value) ? 2 : 1;
    }

    private static bool IsEastAsianWideOrFullwidth(int codePoint)
    {
        int low = 0;
        int high = EastAsianWideAndFullwidthRanges.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            (int start, int end) = EastAsianWideAndFullwidthRanges[mid];
            if (codePoint < start)
            {
                high = mid - 1;
            }
            else if (codePoint > end)
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
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
