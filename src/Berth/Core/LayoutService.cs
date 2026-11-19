using System.IO;
using System.Text.Json;
using Berth.Core.Model;

namespace Berth.Core;

/// <summary>
/// The operations on layouts (SPEC §15), one at a time across processes through <see cref="OperationLock"/>, each
/// returning one <see cref="OperationResult"/> and writing one log event.
/// </summary>
internal sealed class LayoutService(
    IDesktopIcons desktop,
    IMonitors monitors,
    LayoutStore store,
    BerthPaths paths,
    OperationLock gate,
    Log log,
    TimeProvider clock)
{
    /// <summary>
    /// Reads the icons and the monitors and writes the current arrangement's layout (SPEC §15 "Save"): a first save
    /// names it "1 monitor" or "N monitors" (numbered when taken), a later one keeps its name and moves the earlier
    /// icons and time into <c>previous</c>. A given <paramref name="name"/> is validated and stored trimmed.
    /// </summary>
    public OperationResult Save(string? name = null) => Logged("Save", () => SaveLocked(name), origin: null);

    /// <summary>
    /// Puts the current arrangement's saved icons back (SPEC §15 "Restore"): refused, in this order, when the desktop
    /// cannot be reached, when the arrangement has no layout (or its file cannot be read), or while auto arrange is
    /// on. Otherwise it writes <c>undo.json</c> with the positions now, moves every saved icon still on the desktop,
    /// reads the desktop back and counts. <paramref name="origin"/> (an automatic restore's trigger) rides on the
    /// result and its log line.
    /// </summary>
    public OperationResult Restore(string? origin = null) => Logged("Restore", () => RestoreLocked(origin), origin);

    /// <summary>
    /// Whether <c>undo.json</c> holds a valid snapshot for the monitors connected now, without touching the desktop.
    /// </summary>
    public bool CanUndo
    {
        get
        {
            if (ReadUndoSnapshot() is not { } snapshot)
            {
                return false;
            }

            try
            {
                return ArrangementKey.From(monitors.Read()) == snapshot.Key;
            }
            catch (DesktopUnavailableException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Puts the icons back where they were before the last restore (SPEC §15 "Undo"): refused, in this order, when the
    /// desktop cannot be reached, when there is nothing to undo for the current arrangement, or while auto arrange is
    /// on. Deletes <c>undo.json</c> only when every snapshot icon still on the desktop matches its saved position.
    /// </summary>
    public OperationResult Undo() => Logged("Undo", UndoLocked, origin: null);

    /// <summary>
    /// Renames the saved layout of <paramref name="key"/> to trimmed <paramref name="name"/> (SPEC §10), validating
    /// against the other layouts' names and reading the file back before reporting.
    /// </summary>
    public OperationResult Rename(string key, string name) => Logged("Rename", () => RenameLocked(key, name), origin: null);

    /// <summary>
    /// Swaps the saved layout of <paramref name="key"/> with its <c>previous</c> save (SPEC §10) and reads the file
    /// back before reporting; moves no icons and leaves <c>undo.json</c> untouched.
    /// </summary>
    public OperationResult Revert(string key) => Logged("Revert", () => RevertLocked(key), origin: null);

    /// <summary>
    /// Deletes the saved layout of <paramref name="key"/> (SPEC §10) and reads the folder back before reporting; moves
    /// no icons and leaves <c>undo.json</c> untouched.
    /// </summary>
    public OperationResult Delete(string key) => Logged("Delete", () => DeleteLocked(key), origin: null);

    /// <summary>Lets a test act between an operation's read of the layouts and its write. Production leaves it null.</summary>
    internal Action? BeforeWrite { get; init; }

    /// <summary>Lets a test act after a write or delete and before the post-operation read-back. Production leaves it null.</summary>
    internal Action? AfterWrite { get; init; }

    private OperationResult SaveLocked(string? name)
    {
        if (Capture() is not ({ } arrangement, { } icons))
        {
            return OperationResult.Refused(Refusal.DesktopUnavailable);
        }

        string key = ArrangementKey.From(arrangement);
        string file = paths.LayoutFile(key);
        LayoutSnapshot snapshot;
        LayoutSet? all = null;
        try
        {
            snapshot = store.Snapshot(key);

            // The other layouts' names are needed only to name this one: a repeat save keeps its name.
            if (name is not null || snapshot.Layout is null)
            {
                all = store.LoadAll();
            }
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.ReadFailed, paths.LayoutsFolder, error.Message);
        }

        // A layout another program holds may carry the very name: no name is chosen past it.
        if (all?.Unreadable.FirstOrDefault(u => u.Held) is { } held)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: held.FileName, detail: held.Reason);
        }

        // Names of the other arrangements' layouts: this one's own name may be kept or given again.
        string[] others = all is null ? [] : [.. all.Layouts.Where(l => l.Key != key).Select(l => l.Name)];
        string layoutName;
        if (name is not null)
        {
            NameError problem = LayoutNames.Validate(name, others);
            if (problem != NameError.None)
            {
                return OperationResult.Refused(Refusal.InvalidName, nameError: problem);
            }

            layoutName = LayoutNames.Trimmed(name);
        }
        else
        {
            layoutName = snapshot.Layout?.Name ?? LayoutNames.Default(arrangement.Count, others);
        }

        // Written in whole UTC seconds by the layout format itself (SPEC §14).
        DateTimeOffset now = clock.GetUtcNow();
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            key,
            layoutName,
            now,
            [.. arrangement],
            [.. icons],
            snapshot.Layout is { } earlier ? new PreviousSave(earlier.SavedAt, earlier.Icons) : null);
        BeforeWrite?.Invoke();
        try
        {
            store.Save(layout, snapshot);
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
        }
        catch (TargetChangedException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, "The layout file changed while it was being saved. It was left as it is.", layoutName);
        }
        catch (TargetAppearedException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, "A layout file appeared while it was being saved. It was left as it is.", layoutName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, error.Message, layoutName);
        }

        return OperationResult.Saved(layoutName, icons.Count, replacedEarlierSave: snapshot.Layout is not null);
    }

    private OperationResult RestoreLocked(string? origin)
    {
        // The arrangement is checked again before the way back is written; a change there starts over.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (RestoreOnce(origin) is { } result)
            {
                return result;
            }
        }

        return OperationResult.Refused(Refusal.DesktopUnavailable);
    }

    /// <summary>One restore, or null when the displays changed before anything was written, to start over.</summary>
    private OperationResult? RestoreOnce(string? origin)
    {
        if (Capture() is not ({ } arrangement, { } icons))
        {
            return OperationResult.Refused(Refusal.DesktopUnavailable);
        }

        string key = ArrangementKey.From(arrangement);
        Layout? layout;
        try
        {
            layout = store.Find(key);
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.ReadFailed, paths.LayoutsFolder, error.Message);
        }

        if (layout is null)
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        try
        {
            if (desktop.AutoArrange)
            {
                return OperationResult.Refused(Refusal.AutoArrange, layout.Name);
            }
        }
        catch (DesktopUnavailableException)
        {
            return OperationResult.Refused(Refusal.DesktopUnavailable, layout.Name);
        }

        if (!StillThe(key))
        {
            return null;
        }

        var undo = new UndoSnapshot(UndoSnapshot.CurrentSchemaVersion, key, clock.GetUtcNow(), [.. icons]);
        if (LayoutRules.CheckUndo(undo) is { } problem)
        {
            string message = $"This undo snapshot must not be saved: {problem}";
            return OperationResult.Failed(FailureKind.Unexpected, path: null, message, layout.Name, new InvalidOperationException(message));
        }

        // The way back is staged beside undo.json, then put in its place just before anything moves, so `berth undo`
        // in another process has it, and an abort before the move leaves the earlier undo exactly as it was.
        string staged = paths.UndoFile + ".next";
        try
        {
            BerthPaths.EnsureFolderFor(paths.UndoFile);
            JsonFile.Write(staged, undo, BerthJson.Default.UndoSnapshot);

            // A change after the snapshot was taken: nothing has moved; stop rather than place another arrangement's icons.
            if (!StillThe(key))
            {
                return OperationResult.Failed(FailureKind.DisplaysChanged, path: null, "The displays changed before any icon was moved.", layout.Name);
            }

            File.Move(staged, paths.UndoFile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, paths.UndoFile, error.Message, layout.Name);
        }
        finally
        {
            DeleteIfThere(staged);
        }

        IReadOnlyDictionary<string, IconPosition> live = LayoutComparison.ById(icons);
        IconPosition[] wanted = [.. layout.Icons.Where(saved => live.ContainsKey(saved.Id))];
        IReadOnlyList<IconPosition> after;
        try
        {
            desktop.SetPositions(wanted);
            after = desktop.ReadIcons();
        }
        catch (DesktopUnavailableException)
        {
            // Some icons may have moved and none can be counted: the snapshot stays, so undo still works.
            return OperationResult.Failed(
                FailureKind.DesktopUnavailable, path: null, "The desktop could not be reached while the icons were being placed. Undo puts back what was there.", layout.Name);
        }

        IReadOnlyDictionary<string, IconPosition> now = LayoutComparison.ById(after);
        IconState[] states = [.. layout.Icons.Select(saved => LayoutComparison.StateOf(saved, now.GetValueOrDefault(saved.Id)))];
        int inPlace = states.Count(s => s == IconState.InPlace), notPlaced = states.Count(s => s == IconState.Moved), missing = states.Count(s => s == IconState.Missing);

        return OperationResult.Restored(layout.Name, layout.Icons.Length, inPlace, notPlaced, missing, origin);
    }

    private OperationResult UndoLocked()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (UndoOnce() is { } result)
            {
                return result;
            }
        }

        return OperationResult.Refused(Refusal.DesktopUnavailable);
    }

    /// <summary>One undo, or null when the displays changed before anything moved, to start over.</summary>
    private OperationResult? UndoOnce()
    {
        if (Capture() is not ({ } arrangement, { } icons))
        {
            return OperationResult.Refused(Refusal.DesktopUnavailable);
        }

        string key = ArrangementKey.From(arrangement);
        if (ReadUndoSnapshot() is not { } snapshot || snapshot.Key != key)
        {
            return OperationResult.Refused(Refusal.NothingToUndo);
        }

        try
        {
            if (desktop.AutoArrange)
            {
                return OperationResult.Refused(Refusal.AutoArrange);
            }
        }
        catch (DesktopUnavailableException)
        {
            return OperationResult.Refused(Refusal.DesktopUnavailable);
        }

        IReadOnlyDictionary<string, IconPosition> live = LayoutComparison.ById(icons);
        IconPosition[] wanted = [.. snapshot.Icons.Where(saved => live.ContainsKey(saved.Id))];
        if (!StillThe(key))
        {
            return null;
        }

        IReadOnlyList<MonitorInfo> beforeReadBack;
        IReadOnlyList<IconPosition> after;
        IReadOnlyList<MonitorInfo> afterReadBack;
        try
        {
            desktop.SetPositions(wanted);
            beforeReadBack = monitors.Read();
            if (ArrangementKey.From(beforeReadBack) != key)
            {
                return OperationResult.Failed(
                    FailureKind.DisplaysChanged, path: null, "The displays changed before the undo could be checked.");
            }

            after = desktop.ReadIcons();
            afterReadBack = monitors.Read();
        }
        catch (DesktopUnavailableException)
        {
            return OperationResult.Failed(
                FailureKind.DesktopUnavailable, path: null, "The desktop could not be reached while the icons were being placed.");
        }

        if (ArrangementKey.From(afterReadBack) != key)
        {
            return OperationResult.Failed(
                FailureKind.DisplaysChanged, path: null, "The displays changed before the undo could be checked.");
        }

        IReadOnlyDictionary<string, IconPosition> now = LayoutComparison.ById(after);
        IconState[] states = [.. snapshot.Icons.Select(saved => LayoutComparison.StateOf(saved, now.GetValueOrDefault(saved.Id)))];
        int inPlace = states.Count(s => s == IconState.InPlace), notPlaced = states.Count(s => s == IconState.Moved), missing = states.Count(s => s == IconState.Missing);
        if (notPlaced == 0)
        {
            try
            {
                if ((File.GetAttributes(paths.UndoFile) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"'{paths.UndoFile}' is a link, which Berth never deletes.");
                }

                File.Delete(paths.UndoFile);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                // Already absent.
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return OperationResult.Failed(FailureKind.WriteFailed, paths.UndoFile, error.Message);
            }
        }

        return OperationResult.Undone(snapshot.Icons.Length, inPlace, notPlaced, missing);
    }

    private OperationResult RenameLocked(string key, string name)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(name);
        if (!BerthPaths.IsKey(key))
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        if (!TryReadSnapshot(key, out LayoutSnapshot? snapshot, out OperationResult? failure))
        {
            return failure!;
        }

        if (snapshot!.Layout is not { } current)
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        NameError shapeProblem = LayoutNames.Validate(name, []);
        if (shapeProblem != NameError.None)
        {
            return OperationResult.Refused(Refusal.InvalidName, nameError: shapeProblem);
        }

        LayoutSet all;
        try
        {
            all = store.LoadAll();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.ReadFailed, paths.LayoutsFolder, error.Message);
        }

        if (all.Unreadable.FirstOrDefault(u => u.Held) is { } held)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: held.FileName, detail: held.Reason);
        }

        string[] others = [.. all.Layouts.Where(l => l.Key != key).Select(l => l.Name)];
        NameError problem = LayoutNames.Validate(name, others);
        if (problem != NameError.None)
        {
            return OperationResult.Refused(Refusal.InvalidName, nameError: problem);
        }

        string trimmed = LayoutNames.Trimmed(name);
        Layout renamed = current with { Name = trimmed };
        return SaveAndVerify(key, renamed, snapshot, () => OperationResult.Renamed(trimmed));
    }

    private OperationResult RevertLocked(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!BerthPaths.IsKey(key))
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        if (!TryReadSnapshot(key, out LayoutSnapshot? snapshot, out OperationResult? failure))
        {
            return failure!;
        }

        if (snapshot!.Layout is not { } current)
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        if (current.Previous is not { } previous)
        {
            return OperationResult.Refused(Refusal.NoPreviousSave);
        }

        Layout reverted = current with
        {
            SavedAt = previous.SavedAt,
            Icons = previous.Icons,
            Previous = new PreviousSave(current.SavedAt, current.Icons),
        };
        return SaveAndVerify(key, reverted, snapshot, () => OperationResult.Reverted(current.Name, previous.SavedAt));
    }

    private OperationResult DeleteLocked(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!BerthPaths.IsKey(key))
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        if (!TryReadSnapshot(key, out LayoutSnapshot? snapshot, out OperationResult? failure))
        {
            return failure!;
        }

        if (snapshot!.Layout is not { } current)
        {
            return OperationResult.Refused(Refusal.NoLayout);
        }

        string file = paths.LayoutFile(key);
        BeforeWrite?.Invoke();
        try
        {
            store.Delete(snapshot);
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
        }
        catch (TargetChangedException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, "The layout file changed before it could be deleted. It was left as it is.", current.Name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, error.Message, current.Name);
        }

        AfterWrite?.Invoke();
        try
        {
            Layout? remaining = store.Find(key);
            if (remaining is not null)
            {
                return OperationResult.Failed(FailureKind.WriteFailed, file, "The layout file is still there after it was deleted.", current.Name);
            }
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, unreadable.Reason, current.Name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, error.Message, current.Name);
        }

        return OperationResult.Deleted(current.Name);
    }

    private bool TryReadSnapshot(string key, out LayoutSnapshot? snapshot, out OperationResult? failure)
    {
        try
        {
            snapshot = store.Snapshot(key);
            failure = null;
            return true;
        }
        catch (UnreadableLayoutException unreadable)
        {
            snapshot = null;
            failure = OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            snapshot = null;
            failure = OperationResult.Failed(FailureKind.ReadFailed, paths.LayoutsFolder, error.Message);
            return false;
        }
    }

    private OperationResult SaveAndVerify(string key, Layout updated, LayoutSnapshot snapshot, Func<OperationResult> onMatch)
    {
        string file = paths.LayoutFile(key);
        BeforeWrite?.Invoke();
        try
        {
            store.Save(updated, snapshot);
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
        }
        catch (TargetChangedException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, "The layout file changed while it was being saved. It was left as it is.", updated.Name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, error.Message, updated.Name);
        }

        AfterWrite?.Invoke();
        Layout? readBack;
        try
        {
            readBack = store.Find(key);
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, unreadable.Reason, updated.Name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(FailureKind.WriteFailed, file, error.Message, updated.Name);
        }

        return updated.Equals(readBack)
            ? onMatch()
            : OperationResult.Failed(FailureKind.WriteFailed, file, "The layout file read back after saving does not match what was written.", updated.Name);
    }

    private UndoSnapshot? ReadUndoSnapshot()
    {
        byte[] bytes;
        try
        {
            if ((File.GetAttributes(paths.UndoFile) & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            using var stream = new FileStream(paths.UndoFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (!JsonFile.IsTheFileItself(stream, paths.UndoFile, exactName: false) || stream.Length > JsonFile.MaxBytes)
            {
                return null;
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            UndoSnapshot snapshot = BerthJson.Parse(bytes, BerthJson.Default.UndoSnapshot);
            return LayoutRules.CheckUndo(snapshot) is null ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Removing a leftover staging file is best effort; the next restore replaces it.")]
    private static void DeleteIfThere(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Left behind; the next restore writes over it.
        }
    }

    /// <summary>Whether the monitors still give <paramref name="key"/>; an unreachable desktop counts as a change.</summary>
    private bool StillThe(string key)
    {
        try
        {
            return ArrangementKey.From(monitors.Read()) == key;
        }
        catch (DesktopUnavailableException)
        {
            // Unreachable now: the next capture says so as a refusal.
            return false;
        }
    }

    /// <summary>
    /// The monitors and the icons read together: the monitors are read again after the icons, and a capture across a
    /// display change (the icons Explorer moved for another arrangement) is taken again, up to three times. Null
    /// when the desktop cannot be reached or the displays keep changing.
    /// </summary>
    private (IReadOnlyList<MonitorInfo>, IReadOnlyList<IconPosition>)? Capture()
    {
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IReadOnlyList<MonitorInfo> before = monitors.Read();
                IReadOnlyList<IconPosition> icons = desktop.ReadIcons();
                if (ArrangementKey.From(monitors.Read()) == ArrangementKey.From(before))
                {
                    return (before, icons);
                }
            }
        }
        catch (DesktopUnavailableException)
        {
            // An unreachable desktop is a refusal (SPEC §18), which the caller reports.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Runs <paramref name="work"/> under the operation lock (refused as busy when it is not free), turns an exception
    /// nobody expected into a failure that keeps it (SPEC §18 "Something went wrong"), and logs the one result.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Every operation ends in one result and one log event; an unexpected error is that failure (SPEC §18).")]
    private OperationResult Logged(string operation, Func<OperationResult> work, string? origin)
    {
        OperationResult result;
        try
        {
            // Taking the lock is inside the boundary too: a mutex that cannot be opened is a failure, not a throw.
            result = gate.Run(work, () => OperationResult.Refused(Refusal.Busy));
        }
        catch (Exception error)
        {
            result = OperationResult.Failed(FailureKind.Unexpected, path: null, error.Message, error: error);
        }

        // An automatic operation's trigger rides on every result, a refusal or failure included.
        if (origin is not null)
        {
            result = result with { Origin = origin };
        }

        log.Result(result, Describe(operation, result));
        return result;
    }

    /// <summary>The log text until the messages of SPEC §16 and §18 word it (D01 T01 §16).</summary>
    private static string Describe(string operation, OperationResult result) =>
        $"{operation}: {result.Kind}"
        + (result.LayoutName is null ? string.Empty : $" \"{result.LayoutName}\"")
        + (result.Kind == OperationKind.Saved ? $", {Formats.IconCount(result.Total)}" : string.Empty)
        + (result.SavedAt is null ? string.Empty : $", {result.SavedAt.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}")
        + (result.Refusal == Refusal.None ? string.Empty : $", {result.Refusal}")
        + (result.NameError == NameError.None ? string.Empty : $" ({result.NameError})")
        + (result.Path is null ? string.Empty : $", {result.Path}")
        + (result.Kind is OperationKind.Restored or OperationKind.Undone or OperationKind.Failed && result.Total > 0
            ? $", {result.InPlace} of {result.Total} in place, {result.NotPlaced} not placed, {result.Missing} missing" : string.Empty)
        + (result.Detail is null ? string.Empty : $": {result.Detail}")
        + (result.Origin is null ? string.Empty : $" ({result.Origin})");
}
