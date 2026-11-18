using System.IO;
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
    public OperationResult Save(string? name = null) => Logged("Save", () => SaveLocked(name));

    /// <summary>Lets a test act between the save's read of the layouts and its write. Production leaves it null.</summary>
    internal Action? BeforeWrite { get; init; }

    private OperationResult SaveLocked(string? name)
    {
        IReadOnlyList<MonitorInfo> arrangement;
        IReadOnlyList<IconPosition> icons;
        try
        {
            arrangement = monitors.Read();
            icons = desktop.ReadIcons();
        }
        catch (DesktopUnavailableException)
        {
            return OperationResult.Refused(Refusal.DesktopUnavailable);
        }

        string key = ArrangementKey.From(arrangement);
        string file = paths.LayoutFile(key);
        LayoutSnapshot snapshot;
        LayoutSet all;
        try
        {
            snapshot = store.Snapshot(key);
            all = store.LoadAll();
        }
        catch (UnreadableLayoutException unreadable)
        {
            return OperationResult.Refused(Refusal.UnreadableLayout, path: unreadable.FileName, detail: unreadable.Reason);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(paths.LayoutsFolder, error.Message);
        }

        // Names of the other arrangements' layouts: this one's own name may be kept or given again.
        string[] others = [.. all.Layouts.Where(l => l.Key != key).Select(l => l.Name)];
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
            return OperationResult.Failed(file, "The layout file changed while it was being saved. It was left as it is.", layoutName);
        }
        catch (TargetAppearedException)
        {
            return OperationResult.Failed(file, "A layout file appeared while it was being saved. It was left as it is.", layoutName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failed(file, error.Message, layoutName);
        }

        return OperationResult.Saved(layoutName, icons.Count, replacedEarlierSave: snapshot.Layout is not null);
    }

    /// <summary>
    /// Runs <paramref name="work"/> under the operation lock (refused as busy when it is not free), turns an exception
    /// nobody expected into a failure that keeps it (SPEC §18 "Something went wrong"), and logs the one result.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Every operation ends in one result and one log event; an unexpected error is that failure (SPEC §18).")]
    private OperationResult Logged(string operation, Func<OperationResult> work)
    {
        OperationResult result = gate.Run(
            () =>
            {
                try
                {
                    return work();
                }
                catch (Exception error)
                {
                    return OperationResult.Failed(path: null, error.Message, error: error);
                }
            },
            () => OperationResult.Refused(Refusal.Busy));
        log.Result(result, Describe(operation, result));
        return result;
    }

    /// <summary>The log text until the messages of SPEC §16 and §18 word it (D01 T01 §16).</summary>
    private static string Describe(string operation, OperationResult result) =>
        $"{operation}: {result.Kind}"
        + (result.LayoutName is null ? string.Empty : $" \"{result.LayoutName}\"")
        + (result.Kind == OperationKind.Saved ? $", {result.Total} icons" : string.Empty)
        + (result.Refusal == Refusal.None ? string.Empty : $", {result.Refusal}")
        + (result.NameError == NameError.None ? string.Empty : $" ({result.NameError})")
        + (result.Path is null ? string.Empty : $", {result.Path}")
        + (result.Detail is null ? string.Empty : $": {result.Detail}");
}
