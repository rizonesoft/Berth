using System.Collections.Immutable;

namespace Berth.Core.Model;

/// <summary>
/// <c>undo.json</c> (SPEC §14): the positions read just before the last restore, for the arrangement whose key it
/// carries, and when they were read. Berth's own scratch file: one that cannot be read counts as missing.
/// </summary>
internal sealed record UndoSnapshot(int SchemaVersion, string Key, DateTimeOffset TakenAt, ImmutableArray<IconPosition> Icons)
{
    public const int CurrentSchemaVersion = 1;

    public bool Equals(UndoSnapshot? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Key == other.Key
        && TakenAt == other.TakenAt
        && Icons.SequenceEqual(other.Icons);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, Key, TakenAt, Sequence.Hash(Icons));
}
