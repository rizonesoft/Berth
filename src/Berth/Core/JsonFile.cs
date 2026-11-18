using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
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
internal static partial class JsonFile
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

    /// <summary>
    /// As <see cref="Write{T}(string, T, JsonTypeInfo{T})"/> for a caller that has just found no file at
    /// <paramref name="path"/>: the final step never overwrites, so a file that appeared since (a profile sync, another
    /// program) is kept and the write fails with <see cref="TargetAppearedException"/> for the caller to look at it.
    /// </summary>
    public static void WriteNew<T>(string path, T value, JsonTypeInfo<T> info, Action<string>? afterFlush = null) =>
        Write(path, value, info, afterFlush, mustBeNew: true);

    /// <summary>As <see cref="Write{T}(string, T, JsonTypeInfo{T})"/>; <paramref name="afterFlush"/> lets a test
    /// reach the flushed temporary file before it is read back. Production passes null.</summary>
    /// <summary>
    /// As <see cref="Write{T}(string, T, JsonTypeInfo{T})"/> for a caller that read <paramref name="expected"/> from
    /// <paramref name="path"/> and decided to replace it: if the file is anything else by the time it is replaced (a
    /// profile sync, another program), the file that arrived is put back as it was and the write fails with
    /// <see cref="TargetChangedException"/>.
    /// </summary>
    public static void WriteOver<T>(string path, T value, JsonTypeInfo<T> info, byte[] expected, Action<string>? afterFlush = null) =>
        Write(path, value, info, afterFlush, mustBeNew: false, expected);

    /// <summary>As <see cref="WriteOver"/>; <paramref name="afterReplace"/> lets a test act between the replace and its check.</summary>
    internal static void WriteOver<T>(string path, T value, JsonTypeInfo<T> info, byte[] expected, Action<string>? afterFlush, Action? afterReplace) =>
        Write(path, value, info, afterFlush, mustBeNew: false, expected, afterReplace);

    internal static void Write<T>(string path, T value, JsonTypeInfo<T> info, Action<string>? afterFlush, bool mustBeNew = false, byte[]? expected = null, Action? afterReplace = null)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, info);
        if (bytes.Length > MaxBytes)
        {
            // A save Berth could not read back must never replace the last one it can.
            throw new IOException($"'{path}' would be {bytes.Length} bytes, more than the {MaxBytes} a Berth file can hold.");
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException($"'{path}' has no folder.", nameof(path));
        RemoveStaleTemporaries(folder, Path.GetFileName(path));
        string temp = Path.Combine(folder, Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N"));
        bool replaced = false;
        try
        {
            using (FileStream stream = CreateTemporary(temp, path))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            afterFlush?.Invoke(temp);
            if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(bytes))
            {
                throw new IOException($"The copy of '{path}' read back from disk differs from what was written.");
            }

            if (mustBeNew)
            {
                MoveNew(temp, path);
            }
            else if (expected is not null)
            {
                ReplaceExpected(temp, path, expected, bytes, afterReplace);
            }
            else
            {
                Replace(temp, path);
            }

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
    /// Creates the temporary file. When a target exists the staged copy can be read and written by the writing user
    /// alone (one explicit allow, shut off from inheritance), so it is never readable more widely than the target,
    /// whatever the target's or the folder's rules are, even after a crash. The target's own descriptor is what the
    /// final file keeps (<see cref="Replace"/>). A first save takes the folder's rules, as any new file does.
    /// </summary>
    private static FileStream CreateTemporary(string temp, string path)
    {
        if (!File.Exists(path))
        {
            return new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier writer = identity.User
            ?? throw new IOException("The writing account has no security identifier.");
        var ownerOnly = new FileSecurity();
        ownerOnly.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ownerOnly.AddAccessRule(new FileSystemAccessRule(writer, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(temp).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.Read, FileShare.None, 4096, FileOptions.None, ownerOnly);
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

    /// <summary>The suffix of a version a save displaced and could not prove it may drop; never cleaned up automatically.</summary>
    public const string KeptSuffix = ".berth-replaced-";

    /// <summary>
    /// Replaces <paramref name="path"/>, keeping what it held under a <see cref="KeptSuffix"/> name in the same step.
    /// That version is deleted only when it is exactly what the caller read; otherwise it goes back in place and the
    /// write fails, and whatever the rollback displaces is in turn kept unless it is exactly this write. Windows cannot
    /// stop another program writing the file meanwhile, but no version is lost: a crash at any point leaves the
    /// displaced one beside the file, where no clean-up deletes it.
    /// </summary>
    private static void ReplaceExpected(string temp, string path, byte[] expected, byte[] written, Action? afterReplace)
    {
        string kept = path + KeptSuffix + Guid.NewGuid().ToString("N");
        File.Replace(temp, path, kept, ignoreMetadataErrors: false);
        afterReplace?.Invoke();
        if (HoldsExactly(kept, expected))
        {
            File.Delete(kept);
            return;
        }

        string displaced = path + KeptSuffix + Guid.NewGuid().ToString("N");
        File.Replace(kept, path, displaced, ignoreMetadataErrors: false);
        if (HoldsExactly(displaced, written))
        {
            File.Delete(displaced);
        }

        throw new TargetChangedException(path);
    }

    /// <summary>Whether <paramref name="path"/> holds exactly <paramref name="bytes"/>, read only when the lengths agree.</summary>
    private static bool HoldsExactly(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != bytes.Length)
        {
            return false;
        }

        byte[] actual = new byte[bytes.Length];
        stream.ReadExactly(actual);
        return actual.AsSpan().SequenceEqual(bytes);
    }

    private static void MoveNew(string temp, string path)
    {
        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException error) when (File.Exists(path))
        {
            throw new TargetAppearedException(path, error);
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
            // Only a name this writer makes (".tmp-" and a 32-digit GUID): a user's own backup is never touched.
            string suffix = Path.GetFileName(stale)[(fileName.Length + ".tmp-".Length)..];
            if (TemporarySuffix().IsMatch(suffix) && File.GetLastWriteTimeUtc(stale) < cutoff)
            {
                File.Delete(stale);
            }
        }
    }

    [GeneratedRegex(@"^[0-9a-f]{32}\z")]
    private static partial Regex TemporarySuffix();

    /// <summary>
    /// Reads <paramref name="path"/> through <see cref="BerthJson.Parse"/>. False with <see cref="Missing"/> when the
    /// file or its folder does not exist; false with the system's or the parser's message when it cannot be opened
    /// or parsed. It never deletes or rewrites the file.
    /// </summary>
    public static bool TryRead<T>(string path, JsonTypeInfo<T> info, out T? value, out string? reason) where T : class =>
        TryRead(path, info, out value, out reason, out _);

    /// <summary>As <see cref="TryRead{T}(string, JsonTypeInfo{T}, out T, out string)"/>, also giving the bytes read
    /// (null when the file could not be opened), for <see cref="WriteOver"/>.</summary>
    public static bool TryRead<T>(string path, JsonTypeInfo<T> info, out T? value, out string? reason, out byte[]? read) where T : class
    {
        value = null;
        read = null;
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
            read = bytes;
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

/// <summary>A file appeared where a caller had just found none; it was left as it is.</summary>
internal sealed class TargetAppearedException : IOException
{
    public TargetAppearedException(string path, Exception innerException)
        : base($"'{path}' appeared while it was being written for the first time, and was left as it is.", innerException)
    {
    }

    public TargetAppearedException()
    {
    }

    public TargetAppearedException(string message)
        : base(message)
    {
    }
}

/// <summary>The file changed between the caller reading it and the write replacing it; what arrived was kept.</summary>
internal sealed class TargetChangedException : IOException
{
    public TargetChangedException(string path)
        : base($"'{path}' changed while it was being saved, and the version that arrived was kept.")
    {
    }

    public TargetChangedException()
    {
    }

    public TargetChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}