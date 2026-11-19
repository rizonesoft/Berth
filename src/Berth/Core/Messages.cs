using System.Globalization;
using System.IO;
using System.Text;

namespace Berth.Core;

/// <summary>
/// Every sentence of SPEC §8, §10, §13, §16 and §18 in one place, so the window, the tray and the command line word
/// every result, comparison, refusal and log line alike.
/// </summary>
internal static class Messages
{
    public const string Working = "Working";
    public const string Unsaved = "This monitor arrangement has no saved layout yet.";
    public const string UnreadableSettings = "The settings file settings.json could not be read. It was left untouched.";

    public const string AutoArrangeWouldNotTurnOff =
        "Windows did not turn auto arrange off. Turn it off from the desktop: right-click, View, Auto arrange icons.";

    public const string DesktopLostDuringRestore =
        "The desktop could not be reached while the icons were being placed. Undo puts back what was there.";

    public const string DesktopLostDuringUndo =
        "The desktop could not be reached while the icons were being placed.";

    public const string DisplaysChangedDuringRestore =
        "The displays changed before any icon was moved.";

    public const string DisplaysChangedDuringUndo =
        "The displays changed before the undo could be checked.";

    /// <summary>The status-line sentence for a completed, refused or failed operation (SPEC §8, §18).</summary>
    public static string Status(OperationResult r, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(zone);

        if (r.Kind == OperationKind.Reverted)
        {
            _ = RequireSingleLineToken(r.LayoutName, nameof(r));
            DateTimeOffset savedAt = r.SavedAt
                ?? throw new ArgumentException("A revert result must carry the save time it returned to.", nameof(r));
            return $"Reverted to the save from {Formats.When(savedAt, now, zone)}.";
        }

        return Status(r);
    }

    /// <summary>The status-line sentence for any non-<see cref="OperationKind.Reverted"/> result (SPEC §8, §18).</summary>
    public static string Status(OperationResult r)
    {
        ArgumentNullException.ThrowIfNull(r);

        return r.Kind switch
        {
            OperationKind.Saved => FormatStatusSaved(r),
            OperationKind.Restored => FormatRestored(r, includeLayoutName: false),
            OperationKind.Undone => FormatStatusUndone(r),
            OperationKind.Refused or OperationKind.Failed => Refusal(r),
            OperationKind.Reverted => throw new ArgumentException(
                "A revert status requires the current time and time zone.", nameof(r)),
            OperationKind.Renamed or OperationKind.Deleted => throw new ArgumentException(
                $"Operation kind '{r.Kind}' has no status sentence.", nameof(r)),
            _ => throw new ArgumentException($"Unsupported operation kind '{r.Kind}'.", nameof(r)),
        };
    }

    /// <summary>The SPEC §18 message for a refusal or failure, shared across status line, stderr, balloon and log.</summary>
    public static string Refusal(OperationResult r)
    {
        ArgumentNullException.ThrowIfNull(r);

        return r.Kind switch
        {
            OperationKind.Refused => r.Refusal switch
            {
                Core.Refusal.AutoArrange =>
                    "Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again.",
                Core.Refusal.NoLayout => "This monitor arrangement has no saved layout.",
                Core.Refusal.NoPreviousSave => "This layout has no previous save.",
                Core.Refusal.DesktopUnavailable => "Berth cannot reach the desktop. Explorer may be restarting.",
                Core.Refusal.NothingToUndo => "There is nothing to undo.",
                Core.Refusal.Busy => "Berth is busy with another save or restore. Try again in a moment.",
                Core.Refusal.InvalidName => NameProblem(r.NameError),
                Core.Refusal.UnreadableLayout => UnreadableLayout(
                    r.Path ?? throw new ArgumentException("An unreadable layout refusal must name the file.", nameof(r))),
                _ => throw new ArgumentException($"Unsupported refusal reason '{r.Refusal}'.", nameof(r)),
            },
            OperationKind.Failed => FormatFailure(r),
            _ => throw new ArgumentException(
                $"Operation kind '{r.Kind}' is neither a refusal nor a failure.", nameof(r)),
        };
    }

    /// <summary>The SPEC §13 single-line command output or error sentence for an operation result.</summary>
    public static string Cli(OperationResult r)
    {
        ArgumentNullException.ThrowIfNull(r);

        return r.Kind switch
        {
            OperationKind.Saved => FormatCliSaved(r),
            OperationKind.Restored => FormatRestored(r, includeLayoutName: true),
            OperationKind.Undone => FormatCliUndone(r),
            OperationKind.Failed when r.Failure == FailureKind.NotPlaced => FormatNotPlaced(r, includeLayoutName: true),
            OperationKind.Refused or OperationKind.Failed => Refusal(r),
            OperationKind.Reverted or OperationKind.Renamed or OperationKind.Deleted => throw new ArgumentException(
                $"Operation kind '{r.Kind}' has no CLI sentence.", nameof(r)),
            _ => throw new ArgumentException($"Unsupported operation kind '{r.Kind}'.", nameof(r)),
        };
    }

    /// <summary>
    /// The SPEC §18 / §16 log message (following <see cref="Cli"/> for operation results, adding the window-only
    /// edit sentences, and appending <c> (origin)</c> when present).
    /// </summary>
    public static string Log(OperationResult r)
    {
        ArgumentNullException.ThrowIfNull(r);

        string sentence = r.Kind switch
        {
            OperationKind.Reverted => FormatLogReverted(r),
            OperationKind.Renamed => $"Renamed layout to \"{RequireSingleLineToken(r.LayoutName, nameof(r))}\".",
            OperationKind.Deleted => $"Deleted layout \"{RequireSingleLineToken(r.LayoutName, nameof(r))}\".",
            _ => Cli(r),
        };

        if (r.Origin is null)
        {
            return sentence;
        }

        string origin = RequireSingleLineToken(r.Origin, nameof(r));
        return $"{sentence} ({origin})";
    }

    /// <summary>The live desktop-vs-layout comparison sentence for an active layout (SPEC §8).</summary>
    public static string Comparison(int moved, int missing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(moved);
        ArgumentOutOfRangeException.ThrowIfNegative(missing);

        if (moved == 0 && missing == 0)
        {
            return "Every icon is at its saved position.";
        }

        if (moved > 0 && missing > 0)
        {
            string movedClause = moved == 1
                ? "1 icon has moved"
                : string.Create(CultureInfo.InvariantCulture, $"{moved} icons have moved");
            string missingClause = missing == 1
                ? "1 is"
                : string.Create(CultureInfo.InvariantCulture, $"{missing} are");
            return $"{movedClause} and {missingClause} no longer on the desktop.";
        }

        if (moved > 0)
        {
            return moved == 1
                ? "1 icon has moved since this layout was saved."
                : string.Create(CultureInfo.InvariantCulture, $"{moved} icons have moved since this layout was saved.");
        }

        return missing == 1
            ? "1 saved icon is no longer on the desktop."
            : string.Create(CultureInfo.InvariantCulture, $"{missing} saved icons are no longer on the desktop.");
    }

    /// <summary>The status sentence when a layout saved for another monitor arrangement is selected (SPEC §8).</summary>
    public static string OtherArrangement(DateTimeOffset savedAt, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return $"Saved {Formats.When(savedAt, now, zone)} for another monitor arrangement.";
    }

    /// <summary>The validation sentence shown for a rejected layout name (SPEC §10).</summary>
    public static string NameProblem(NameError error) => error switch
    {
        NameError.Empty => "Enter a name.",
        NameError.TooLong => "A name can be at most 40 characters.",
        NameError.Taken => "Another layout already has this name.",
        _ => throw new ArgumentException($"Unsupported name error '{error}'.", nameof(error)),
    };

    /// <summary>The SPEC §18 sentence for an unreadable or held layout file, reduced to its file name.</summary>
    public static string UnreadableLayout(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Value must not be null or blank.", nameof(fileName));
        }

        // Extract the file name before escaping controls so a backslash in `\uXXXX` is never treated as a separator.
        string name = Path.GetFileName(CollapseLineBreaks(fileName));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An unreadable layout message must include a file name.", nameof(fileName));
        }

        return $"The layout file {EscapeControls(name)} could not be read. It was left untouched.";
    }

    /// <summary>The SPEC §18 sentence when writing <paramref name="path"/> fails.</summary>
    public static string WriteFailed(string path, string reason)
    {
        string cleanPath = RequireSingleLineToken(path, nameof(path));
        return $"Berth could not write {cleanPath}: {CleanSentence(reason)}.";
    }

    /// <summary>The SPEC §18 sentence when reading <paramref name="path"/> (such as the layouts folder) fails.</summary>
    public static string ReadFailed(string path, string reason)
    {
        string cleanPath = RequireSingleLineToken(path, nameof(path));
        return $"Berth could not read {cleanPath}: {CleanSentence(reason)}.";
    }

    /// <summary>The SPEC §18 sentence when changing the Start with Windows registry value fails.</summary>
    public static string StartWithWindowsFailed(string reason) =>
        $"Berth could not change the Start with Windows setting: {CleanSentence(reason)}.";

    /// <summary>The SPEC §18 sentence for an unexpected failure whose full exception is in the log.</summary>
    public static string Unexpected(string message) =>
        $"Something went wrong: {CleanSentence(message)}. The details are in the log.";

    private static string FormatStatusSaved(OperationResult r)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(r.Total);
        _ = RequireSingleLineToken(r.LayoutName, nameof(r));
        string head = $"Saved {Formats.IconCount(r.Total)}.";
        return r.ReplacedEarlierSave ? $"{head} The previous save is kept." : head;
    }

    private static string FormatCliSaved(OperationResult r)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(r.Total);
        string name = RequireSingleLineToken(r.LayoutName, nameof(r));
        return $"Saved {Formats.IconCount(r.Total)} to \"{name}\".";
    }

    private static string FormatRestored(OperationResult r, bool includeLayoutName)
    {
        ValidatePlacementCounts(r);
        string name = RequireSingleLineToken(r.LayoutName, nameof(r));

        if (r.Missing == 0 && r.NotPlaced == 0)
        {
            return includeLayoutName
                ? $"Restored {Formats.IconCount(r.Total)} from \"{name}\"."
                : $"Restored {Formats.IconCount(r.Total)}.";
        }

        return PartialRestore(r, includeLayoutName ? name : null);
    }

    private static string FormatStatusUndone(OperationResult r)
    {
        ValidatePlacementCounts(r);
        RequireNoLayoutName(r);

        if (r.Missing == 0 && r.NotPlaced == 0)
        {
            return r.Total == 1
                ? "Put 1 icon back where it was before the restore."
                : $"Put {Formats.IconCount(r.Total)} back where they were before the restore.";
        }

        return PartialUndo(r);
    }

    private static string FormatCliUndone(OperationResult r)
    {
        ValidatePlacementCounts(r);
        RequireNoLayoutName(r);

        if (r.Missing == 0 && r.NotPlaced == 0)
        {
            return $"Put {Formats.IconCount(r.Total)} back.";
        }

        return PartialUndo(r);
    }

    private static string FormatLogReverted(OperationResult r)
    {
        string name = RequireSingleLineToken(r.LayoutName, nameof(r));
        DateTimeOffset savedAt = r.SavedAt
            ?? throw new ArgumentException("A revert result must carry the save time it returned to.", nameof(r));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Reverted \"{name}\" to the save from {savedAt.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}.");
    }

    private static string FormatFailure(OperationResult r)
    {
        switch (r.Failure)
        {
            case FailureKind.NotPlaced:
                return FormatNotPlaced(r, includeLayoutName: false);

            case FailureKind.WriteFailed:
                return WriteFailed(
                    r.Path ?? throw new ArgumentException("A write failure must name its path.", nameof(r)),
                    r.Detail ?? throw new ArgumentException("A write failure must carry its detail.", nameof(r)));

            case FailureKind.ReadFailed:
                return ReadFailed(
                    r.Path ?? throw new ArgumentException("A read failure must name its path.", nameof(r)),
                    r.Detail ?? throw new ArgumentException("A read failure must carry its detail.", nameof(r)));

            case FailureKind.DesktopUnavailable:
                if (r.LayoutName is null)
                {
                    return DesktopLostDuringUndo;
                }

                _ = RequireSingleLineToken(r.LayoutName, nameof(r));
                return DesktopLostDuringRestore;

            case FailureKind.DisplaysChanged:
                if (r.LayoutName is null)
                {
                    return DisplaysChangedDuringUndo;
                }

                _ = RequireSingleLineToken(r.LayoutName, nameof(r));
                return DisplaysChangedDuringRestore;

            case FailureKind.Unexpected:
                return Unexpected(
                    r.Detail ?? throw new ArgumentException("An unexpected failure must carry its detail.", nameof(r)));

            default:
                throw new ArgumentException($"Unsupported failure kind '{r.Failure}'.", nameof(r));
        }
    }

    private static string FormatNotPlaced(OperationResult r, bool includeLayoutName)
    {
        ValidatePlacementCounts(r);
        if (r.NotPlaced <= 0)
        {
            throw new ArgumentException("A NotPlaced failure must have NotPlaced above zero.", nameof(r));
        }

        if (r.LayoutName is null)
        {
            return PartialUndo(r);
        }

        string name = RequireSingleLineToken(r.LayoutName, nameof(r));
        return PartialRestore(r, includeLayoutName ? name : null);
    }

    private static string PartialRestore(OperationResult r, string? layoutName)
    {
        var text = new StringBuilder();
        if (layoutName is null)
        {
            text.Append(CultureInfo.InvariantCulture, $"Restored {r.InPlace} of {Formats.IconCount(r.Total)}.");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"Restored {r.InPlace} of {Formats.IconCount(r.Total)} from \"{layoutName}\".");
        }

        if (r.NotPlaced > 0)
        {
            text.Append(
                r.NotPlaced == 1
                    ? " 1 did not stay where it was saved."
                    : string.Create(CultureInfo.InvariantCulture, $" {r.NotPlaced} did not stay where they were saved."));
        }

        AppendMissingClause(text, r.Missing);
        return text.ToString();
    }

    private static string PartialUndo(OperationResult r)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Put {r.InPlace} of {Formats.IconCount(r.Total)} back.");

        if (r.NotPlaced > 0)
        {
            text.Append(
                r.NotPlaced == 1
                    ? " 1 did not stay where it was."
                    : string.Create(CultureInfo.InvariantCulture, $" {r.NotPlaced} did not stay where they were."));
        }

        AppendMissingClause(text, r.Missing);
        return text.ToString();
    }

    private static void AppendMissingClause(StringBuilder text, int missing)
    {
        if (missing > 0)
        {
            text.Append(
                missing == 1
                    ? " 1 is no longer on the desktop."
                    : string.Create(CultureInfo.InvariantCulture, $" {missing} are no longer on the desktop."));
        }
    }

    private static void ValidatePlacementCounts(OperationResult r)
    {
        if (r.Total < 0 || r.InPlace < 0 || r.NotPlaced < 0 || r.Missing < 0
            || (long)r.InPlace + r.NotPlaced + r.Missing != r.Total)
        {
            throw new ArgumentException(
                $"Invalid placement counts ({r.InPlace} in place, {r.NotPlaced} not placed, {r.Missing} missing of {r.Total}).",
                nameof(r));
        }
    }

    private static void RequireNoLayoutName(OperationResult r)
    {
        if (r.LayoutName is not null)
        {
            throw new ArgumentException("An undo result must not name a layout.", nameof(r));
        }
    }

    private static string RequireSingleLineToken(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be null or blank.", paramName);
        }

        return EscapeControls(CollapseLineBreaks(value));
    }

    private static string CleanSentence(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string collapsed = CollapseLineBreaks(text);
        int start = 0;
        while (start < collapsed.Length && char.IsWhiteSpace(collapsed[start]))
        {
            start++;
        }

        int end = collapsed.Length;
        while (end > start && (char.IsWhiteSpace(collapsed[end - 1]) || collapsed[end - 1] == '.'))
        {
            end--;
        }

        return start >= end ? "unknown error" : EscapeControls(collapsed[start..end]);
    }

    private static string EscapeControls(string value)
    {
        int first = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return value;
        }

        var escaped = new StringBuilder(value.Length + 8);
        escaped.Append(value, 0, first);
        for (int i = first; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsControl(c))
            {
                escaped.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
            }
            else
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }

    private static string CollapseLineBreaks(string value)
    {
        if (value.IndexOfAny(['\r', '\n']) < 0)
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        bool inBreak = false;
        foreach (char c in value)
        {
            if (c is '\r' or '\n')
            {
                if (!inBreak)
                {
                    result.Append(' ');
                    inBreak = true;
                }
            }
            else
            {
                inBreak = false;
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
