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
    public void Save(Layout layout) => Save(layout, beforeReplace: null);

    /// <summary>As <see cref="Save(Layout)"/>; <paramref name="beforeReplace"/> lets a test act just before the file is put in place.</summary>
    internal void Save(Layout layout, Action? beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string? problem = LayoutRules.Check(layout, layout.Key);
        if (problem is not null)
        {
            throw new ArgumentException($"This layout must not be saved: {problem}", nameof(layout));
        }

        string path = paths.LayoutFile(layout.Key);
        Lookup current = Read(path, layout.Key);
        if (current.Unreadable is { } unreadable)
        {
            throw new UnreadableLayoutException(unreadable.FileName, unreadable.Reason);
        }

        BerthPaths.EnsureFolderFor(path);
        Action<string>? hook = beforeReplace is null ? null : _ => beforeReplace();
        if (current.Layout is not null)
        {
            try
            {
                JsonFile.WriteOver(path, layout, BerthJson.Default.Layout, current.Bytes!, hook, exactName: true);
            }
            catch (TargetChangedException changed) when (changed.InnerException is IOException or UnauthorizedAccessException)
            {
                // It could not be read just before the replace (another program held it): that is the refusal.
                throw new UnreadableLayoutException(Path.GetFileName(path), changed.InnerException.Message);
            }
            catch (TargetChangedException) when (Read(path, layout.Key).Unreadable is { } arrived)
            {
                // The file changed while it was being saved, into one that cannot be read: it was kept; say so.
                throw new UnreadableLayoutException(arrived.FileName, arrived.Reason);
            }

            return;
        }

        try
        {
            JsonFile.WriteNew(path, layout, BerthJson.Default.Layout, hook);
        }
        catch (TargetAppearedException) when (Read(path, layout.Key).Unreadable is { } appeared)
        {
            // A file arrived while this first save was being made, and it cannot be read: keep it and say so.
            throw new UnreadableLayoutException(appeared.FileName, appeared.Reason);
        }
    }

    /// <summary>Every layout file in key order. A fresh profile, with no layouts folder yet, has none.</summary>
    /// <exception cref="IOException">The layouts folder exists but cannot be listed.</exception>
    /// <exception cref="UnauthorizedAccessException">The layouts folder may not be listed.</exception>
    public LayoutSet LoadAll() => LoadAll(afterList: null);

    /// <summary>As <see cref="LoadAll()"/>; <paramref name="afterList"/> lets a test act between listing and reading.</summary>
    internal LayoutSet LoadAll(Action? afterList)
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

        afterList?.Invoke();

        // A save interrupted after its file was moved away leaves only its note: finish it, which keeps the save
        // beside the missing file.
        foreach (string note in files.Where(f => f.EndsWith(".json" + JsonFile.PendingSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            string target = note[..^JsonFile.PendingSuffix.Length];
            if (!files.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    JsonFile.Recover(target, exactName: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Reported when that layout is next read; the listing goes on.
                }
            }
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
            else
            {
                // A file listed a moment ago and gone now is still news: the window names it rather than dropping it.
                unreadable.Add(found.Unreadable ?? new UnreadableFile(Path.GetFileName(path), "It went away while it was being read."));
            }
        }

        return new LayoutSet(layouts, unreadable);
    }

    /// <summary>The layout of <paramref name="key"/>, or null when there is none.</summary>
    /// <exception cref="UnreadableLayoutException">Its file cannot be read, so it must not be used.</exception>
    public Layout? Find(string key) => Find(key, beforeSettledRead: null);

    /// <summary>As <see cref="Find(string)"/>; <paramref name="beforeSettledRead"/> lets a test act after the name and
    /// link checks and the first recovery, just before the file is read.</summary>
    internal Layout? Find(string key, Action? beforeSettledRead)
    {
        Lookup found = Read(paths.LayoutFile(key), key, beforeSettledRead);
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
    private static Lookup Read(string path, string fileKey, Action? beforeSettledRead = null)
    {
        string fileName = Path.GetFileName(path);
        FileInfo? entry = Entry(path);

        // The exact-name and no-link rules come first: a file Berth would refuse is never recovered, read or written.
        if (entry is not null && entry.Name != fileKey + ".json")
        {
            return new Lookup(null, new UnreadableFile(entry.Name, $"Its name is not the layout file name {fileKey}.json."));
        }

        if (entry is not null && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return new Lookup(null, new UnreadableFile(entry.Name, "It is a link, which Berth never writes."));
        }

        try
        {
            // An interrupted save is finished next, even when its file has been moved away since.
            JsonFile.Recover(path, exactName: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new Lookup(null, new UnreadableFile(fileName, $"An earlier save could not be finished: {error.Message}"));
        }

        if (entry is null)
        {
            return new Lookup(null, null);
        }

        beforeSettledRead?.Invoke();
        bool readable;
        Layout? layout;
        string? reason;
        byte[]? bytes;
        try
        {
            readable = JsonFile.TryReadSettled(path, BerthJson.Default.Layout, exactName: true, out layout, out reason, out bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Renamed(path, fileKey) ?? new Lookup(null, new UnreadableFile(fileName, $"It could not be read as it was being saved: {error.Message}"));
        }

        // Read as Windows opens names: if the file was renamed to another case meanwhile, what was read is not
        // Berth's file, and the rule refuses it under the name it now has, whatever its contents.
        if (Renamed(path, fileKey) is { } renamed)
        {
            return renamed;
        }

        if (!readable)
        {
            return new Lookup(null, new UnreadableFile(fileName, reason == JsonFile.Missing ? "It went away while it was being read." : reason!));
        }

        string? problem = LayoutRules.Check(layout!, fileKey);
        return problem is null ? new Lookup(layout, null, bytes) : new Lookup(null, new UnreadableFile(fileName, problem));
    }

    /// <summary>The refusal for a file now at <paramref name="path"/> under another spelling than the key's, or null.</summary>
    private static Lookup? Renamed(string path, string fileKey) =>
        Entry(path) is { } now && now.Name != fileKey + ".json"
            ? new Lookup(null, new UnreadableFile(now.Name, $"Its name is not the layout file name {fileKey}.json."))
            : null;

    /// <summary>The entry at <paramref name="path"/> as the folder lists it (its real name), or null.</summary>
    /// <exception cref="IOException">The folder exists but cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be read.</exception>
    private static FileInfo? Entry(string path)
    {
        try
        {
            // Asked of the folder itself: only a folder that is really not there means "no layout". A folder that
            // cannot be read throws, so no operation mistakes it for a fresh profile and writes past it.
            return new DirectoryInfo(Path.GetDirectoryName(path)!).EnumerateFiles(Path.GetFileName(path)).FirstOrDefault();
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private sealed record Lookup(Layout? Layout, UnreadableFile? Unreadable, byte[]? Bytes = null);
}
