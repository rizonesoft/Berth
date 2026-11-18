namespace Berth.Core;

/// <summary>What an operation did (SPEC §8, §13, §16): the window, the tray and the command line word it alike.</summary>
internal enum OperationKind
{
    Saved,
    Restored,
    Undone,
    Reverted,
    Renamed,
    Deleted,
    Refused,
    Failed,
}

/// <summary>Why an operation was refused before it changed anything.</summary>
internal enum Refusal
{
    None,
    AutoArrange,
    NoLayout,
    DesktopUnavailable,
    NothingToUndo,
    Busy,
    InvalidName,
    UnreadableLayout,
}

/// <summary>
/// The result of one operation. A restore or undo that left icons on the desktop but not where they were wanted
/// (<see cref="NotPlaced"/> above 0) is <see cref="OperationKind.Failed"/>, keeping its counts.
/// </summary>
internal sealed record OperationResult
{
    private OperationResult(OperationKind kind) => Kind = kind;

    public OperationKind Kind { get; }

    public string? LayoutName { get; init; }

    public int Total { get; init; }

    public int InPlace { get; init; }

    /// <summary>On the desktop, but not at the wanted position after read-back.</summary>
    public int NotPlaced { get; init; }

    public int Missing { get; init; }

    public bool ReplacedEarlierSave { get; init; }

    /// <summary>The save a revert returned to.</summary>
    public DateTimeOffset? SavedAt { get; init; }

    /// <summary>Null, or what started it, such as <c>automatic, display change</c>.</summary>
    public string? Origin { get; init; }

    public Refusal Refusal { get; init; }

    public NameError NameError { get; init; }

    public string? Path { get; init; }

    public string? Detail { get; init; }

    /// <summary>The exception behind a <see cref="OperationKind.Failed"/> result nobody expected, for the log.</summary>
    public Exception? Error { get; init; }

    /// <summary>Whether it did what was asked (not refused, not failed).</summary>
    public bool Done => Kind is not (OperationKind.Refused or OperationKind.Failed);

    public static OperationResult Saved(string name, int total, bool replacedEarlierSave) =>
        new(OperationKind.Saved) { LayoutName = name, Total = total, ReplacedEarlierSave = replacedEarlierSave };

    public static OperationResult Restored(string name, int total, int inPlace, int notPlaced, int missing, string? origin = null) =>
        new(notPlaced > 0 ? OperationKind.Failed : OperationKind.Restored)
        {
            LayoutName = name, Total = total, InPlace = inPlace, NotPlaced = notPlaced, Missing = missing, Origin = origin,
        };

    public static OperationResult Undone(int total, int inPlace, int notPlaced, int missing) =>
        new(notPlaced > 0 ? OperationKind.Failed : OperationKind.Undone)
        {
            Total = total, InPlace = inPlace, NotPlaced = notPlaced, Missing = missing,
        };

    public static OperationResult Reverted(string name, DateTimeOffset savedAt) =>
        new(OperationKind.Reverted) { LayoutName = name, SavedAt = savedAt };

    public static OperationResult Renamed(string name) => new(OperationKind.Renamed) { LayoutName = name };

    public static OperationResult Deleted(string name) => new(OperationKind.Deleted) { LayoutName = name };

    public static OperationResult Refused(Refusal refusal, string? layoutName = null, NameError nameError = NameError.None, string? path = null, string? detail = null) =>
        refusal == Refusal.None
            ? throw new ArgumentException("A refusal names its reason.", nameof(refusal))
            : new(OperationKind.Refused) { Refusal = refusal, LayoutName = layoutName, NameError = nameError, Path = path, Detail = detail };

    public static OperationResult Failed(string? path, string detail, string? layoutName = null, Exception? error = null) =>
        new(OperationKind.Failed) { Path = path, Detail = detail, LayoutName = layoutName, Error = error };
}
