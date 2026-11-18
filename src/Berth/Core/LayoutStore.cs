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
        string[] files;
        try
        {
            files = Directory.GetFiles(paths.LayoutsFolder);
        }
        catch (DirectoryNotFoundException)
        {
            // A fresh profile: nothing has been saved yet. Any other failure to list reaches the caller.
            return new LayoutSet([], []);
        }

        var layouts = new List<Layout>();
        var unreadable = new List<UnreadableFile>();
        foreach (string path in files.Order(StringComparer.OrdinalIgnoreCase))
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

    /// <summary>
    /// What is stored at <paramref name="path"/>, judged the same way for every operation: nothing, a readable
    /// layout, or an unreadable file under the name it really has on disk. An entry whose name is not exactly the
    /// lowercase key, or that is a link (Berth never writes one), is unreadable, so no operation follows a link or a
    /// differently written name that another operation would refuse.
    /// </summary>
    private static Lookup Read(string path, string fileKey)
    {
        string fileName = Path.GetFileName(path);
        FileInfo? entry = new DirectoryInfo(Path.GetDirectoryName(path)!).Exists
            ? new DirectoryInfo(Path.GetDirectoryName(path)!).EnumerateFiles(fileName).FirstOrDefault()
            : null;
        if (entry is null)
        {
            return new Lookup(null, null);
        }

        if (entry.Name != fileKey + ".json")
        {
            return new Lookup(null, new UnreadableFile(entry.Name, $"Its name is not the layout file name {fileKey}.json."));
        }

        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return new Lookup(null, new UnreadableFile(entry.Name, "It is a link, which Berth never writes."));
        }

        if (!JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? layout, out string? reason))
        {
            return new Lookup(null, new UnreadableFile(fileName, reason == JsonFile.Missing ? "It went away while it was being read." : reason!));
        }

        string? problem = LayoutRules.Check(layout!, fileKey);
        return problem is null ? new Lookup(layout, null) : new Lookup(null, new UnreadableFile(fileName, problem));
    }

    private sealed record Lookup(Layout? Layout, UnreadableFile? Unreadable);
}
