using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Berth.Core.Model;

namespace Berth.Core;

/// <summary>
/// The one way Berth writes and reads its JSON files (SPEC §14 "Every write"): a temporary file in the same folder,
/// flushed, read back from disk and compared, and only then moved over the target in one step. A failure at any
/// point leaves the old file as it was and no temporary file behind.
/// </summary>
internal static class JsonFile
{
    /// <summary>The <c>reason</c> <see cref="TryRead"/> gives for a file or folder that does not exist.</summary>
    public const string Missing = "missing";

    /// <summary>
    /// The largest file <see cref="TryRead"/> opens. A layout of ten thousand icons is about 1.5 MB, so a bigger file
    /// is not one of Berth's and is refused before memory is asked for it.
    /// </summary>
    public const long MaxBytes = 16 * 1024 * 1024;

    /// <summary>How old a temporary file of the same target must be before a write treats it as left by a crash.</summary>
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    /// <exception cref="IOException">The write, the read-back comparison or the replace failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or the target may not be written.</exception>
    public static void Write<T>(string path, T value, JsonTypeInfo<T> info) => Write(path, value, info, afterFlush: null);

    /// <summary>As <see cref="Write{T}(string, T, JsonTypeInfo{T})"/>; <paramref name="afterFlush"/> lets a test
    /// reach the flushed temporary file before it is read back. Production passes null.</summary>
    internal static void Write<T>(string path, T value, JsonTypeInfo<T> info, Action<string>? afterFlush)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, info);
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException($"'{path}' has no folder.", nameof(path));
        RemoveStaleTemporaries(folder, Path.GetFileName(path));
        string temp = Path.Combine(folder, Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N"));
        bool replaced = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            afterFlush?.Invoke(temp);
            if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(bytes))
            {
                throw new IOException($"The copy of '{path}' read back from disk differs from what was written.");
            }

            Replace(temp, path);
            replaced = true;
        }
        finally
        {
            if (!replaced)
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Puts <paramref name="temp"/> in place of <paramref name="path"/> in one step. An existing target is replaced
    /// with <see cref="File.Replace(string, string, string?)"/> (ReplaceFileW), which keeps the target's security
    /// descriptor and attributes, so a layout its owner restricted is not opened up by being saved again.
    /// </summary>
    private static void Replace(string temp, string path)
    {
        if (!File.Exists(path))
        {
            File.Move(temp, path, overwrite: false);
            return;
        }

        try
        {
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: false);
        }
        catch (FileNotFoundException) when (!File.Exists(path) && File.Exists(temp))
        {
            // The target went away between the check and the replace: there is nothing to keep, so move.
            File.Move(temp, path, overwrite: false);
        }
    }

    /// <summary>
    /// Deletes temporary files of this target older than <see cref="StaleAfter"/>: a writer ended between its flush
    /// and its replace (a crash, Task Manager) leaves one, and every later write would otherwise leave it there.
    /// </summary>
    private static void RemoveStaleTemporaries(string folder, string fileName)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - StaleAfter;
        foreach (string stale in Directory.EnumerateFiles(folder, fileName + ".tmp-*"))
        {
            if (File.GetLastWriteTimeUtc(stale) < cutoff)
            {
                File.Delete(stale);
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="path"/> through <see cref="BerthJson.Parse"/>. False with <see cref="Missing"/> when the
    /// file or its folder does not exist; false with the system's or the parser's message when it cannot be opened
    /// or parsed. It never deletes or rewrites the file.
    /// </summary>
    public static bool TryRead<T>(string path, JsonTypeInfo<T> info, out T? value, out string? reason) where T : class
    {
        value = null;
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes)
            {
                reason = $"The file is {stream.Length} bytes, more than the {MaxBytes} a Berth file can hold.";
                return false;
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            reason = Missing;
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            reason = error.Message;
            return false;
        }

        try
        {
            value = BerthJson.Parse(bytes, info);
            reason = null;
            return true;
        }
        catch (JsonException error)
        {
            reason = error.Message;
            return false;
        }
    }
}
