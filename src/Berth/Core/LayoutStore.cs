using System.IO;
using Berth.Core.Model;

namespace Berth.Core;

/// <summary>A layout file that cannot be read, by its name in the layouts folder and the reason.</summary>
internal sealed record UnreadableFile(string FileName, string Reason);

/// <summary>Every readable layout, and every layout file that could not be read (SPEC §9, third notice).</summary>
internal sealed record LayoutSet(IReadOnlyList<Layout> Layouts, IReadOnlyList<UnreadableFile> Unreadable);

/// <summary>
/// The layouts folder: one file per monitor arrangement, named by its key (SPEC §14). A file that cannot be read is
/// reported, kept out of every lookup, and never overwritten or deleted.
/// </summary>
internal sealed class LayoutStore(BerthPaths paths)
{
    /// <summary>Writes <paramref name="layout"/> to its arrangement's file.</summary>
    /// <exception cref="ArgumentException">The layout breaks <see cref="LayoutRules"/>.</exception>
    /// <exception cref="UnreadableLayoutException">The file it would replace cannot be read, so it is kept.</exception>
    /// <exception cref="IOException">The folder or the file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or the file may not be written.</exception>
    public void Save(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string? problem = LayoutRules.Check(layout, layout.Key);
        if (problem is not null)
        {
            throw new ArgumentException($"This layout must not be saved: {problem}", nameof(layout));
        }

        string path = paths.LayoutFile(layout.Key);
        if (Read(path, layout.Key) is { Unreadable: { } unreadable })
        {
            throw new UnreadableLayoutException(unreadable.FileName, unreadable.Reason);
        }

        BerthPaths.EnsureFolderFor(path);
        JsonFile.Write(path, layout, BerthJson.Default.Layout);
    }

    /// <summary>Every layout file in key order. A fresh profile, with no layouts folder yet, has none.</summary>
    /// <exception cref="IOException">The layouts folder exists but cannot be listed.</exception>
    /// <exception cref="UnauthorizedAccessException">The layouts folder may not be listed.</exception>
    public LayoutSet LoadAll()
    {
        if (!Directory.Exists(paths.LayoutsFolder) && !File.Exists(paths.LayoutsFolder))
        {
            return new LayoutSet([], []);
        }

        var layouts = new List<Layout>();
        var unreadable = new List<UnreadableFile>();
        foreach (string path in Directory.GetFiles(paths.LayoutsFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Lookup found = Read(path, Path.GetFileNameWithoutExtension(path));
            if (found.Layout is not null)
            {
                layouts.Add(found.Layout);
            }
            else if (found.Unreadable is not null)
            {
                unreadable.Add(found.Unreadable);
            }
        }

        return new LayoutSet(layouts, unreadable);
    }

    /// <summary>The layout of <paramref name="key"/>, or null when there is none.</summary>
    /// <exception cref="UnreadableLayoutException">Its file cannot be read, so it must not be used.</exception>
    public Layout? Find(string key)
    {
        Lookup found = Read(paths.LayoutFile(key), key);
        return found.Unreadable is { } unreadable ? throw new UnreadableLayoutException(unreadable.FileName, unreadable.Reason) : found.Layout;
    }

    /// <summary>Deletes the layout of <paramref name="key"/>; false when there is none.</summary>
    /// <exception cref="UnreadableLayoutException">Its file cannot be read now, so it is kept.</exception>
    /// <exception cref="IOException">The file cannot be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be deleted.</exception>
    public bool Delete(string key)
    {
        string path = paths.LayoutFile(key);
        Lookup found = Read(path, key);
        if (found.Unreadable is { } unreadable)
        {
            throw new UnreadableLayoutException(unreadable.FileName, unreadable.Reason);
        }

        if (found.Layout is null)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static Lookup Read(string path, string fileKey)
    {
        string fileName = Path.GetFileName(path);
        if (!JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? layout, out string? reason))
        {
            return reason == JsonFile.Missing ? new Lookup(null, null) : new Lookup(null, new UnreadableFile(fileName, reason!));
        }

        string? problem = LayoutRules.Check(layout!, fileKey);
        return problem is null ? new Lookup(layout, null) : new Lookup(null, new UnreadableFile(fileName, problem));
    }

    private sealed record Lookup(Layout? Layout, UnreadableFile? Unreadable);
}
