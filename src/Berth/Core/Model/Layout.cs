using System.Collections.Immutable;

namespace Berth.Core.Model;

/// <summary>The one save a layout's current save replaced (SPEC §14 <c>previous</c>).</summary>
internal sealed record PreviousSave(DateTimeOffset SavedAt, ImmutableArray<IconPosition> Icons)
{
    public bool Equals(PreviousSave? other) =>
        other is not null && SavedAt == other.SavedAt && Icons.SequenceEqual(other.Icons);

    public override int GetHashCode() => HashCode.Combine(SavedAt, Sequence.Hash(Icons));
}

/// <summary>A saved layout: one file per monitor arrangement (SPEC §14). Its collections are immutable snapshots.</summary>
internal sealed record Layout(
    int SchemaVersion,
    string Key,
    string Name,
    DateTimeOffset SavedAt,
    ImmutableArray<MonitorInfo> Monitors,
    ImmutableArray<IconPosition> Icons,
    PreviousSave? Previous)
{
    /// <summary>The only <c>schemaVersion</c> this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    public bool Equals(Layout? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Key == other.Key
        && Name == other.Name
        && SavedAt == other.SavedAt
        && Monitors.SequenceEqual(other.Monitors)
        && Icons.SequenceEqual(other.Icons)
        && Equals(Previous, other.Previous);

    public override int GetHashCode() =>
        HashCode.Combine(SchemaVersion, Key, Name, SavedAt, Sequence.Hash(Monitors), Sequence.Hash(Icons), Previous);
}

/// <summary>Content hashing for the immutable arrays the records hold.</summary>
internal static class Sequence
{
    public static int Hash<T>(ImmutableArray<T> items)
    {
        var hash = new HashCode();
        foreach (T item in items.IsDefault ? [] : items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
