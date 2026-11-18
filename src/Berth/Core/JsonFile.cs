using System.Text;
using System.Security.Cryptography;
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
    public static void WriteOver<T>(string path, T value, JsonTypeInfo<T> info, byte[] expected, Action<string>? afterFlush = null, bool exactName = false) =>
        Write(path, value, info, afterFlush, mustBeNew: false, expected, exactName: exactName);

    /// <summary>As <see cref="WriteOver"/>; <paramref name="afterCheck"/> and <paramref name="afterReplace"/> let a test
    /// act just after the last check and just after the replace.</summary>
    internal static void WriteOver<T>(string path, T value, JsonTypeInfo<T> info, byte[] expected, Action<string>? afterFlush, Action? afterCheck, Action? afterReplace, Action? beforeNoteRemoved = null, bool exactName = false) =>
        Write(path, value, info, afterFlush, mustBeNew: false, expected, afterReplace, afterCheck, beforeNoteRemoved, exactName);

    internal static void Write<T>(string path, T value, JsonTypeInfo<T> info, Action<string>? afterFlush, bool mustBeNew = false, byte[]? expected = null, Action? afterReplace = null, Action? afterCheck = null, Action? beforeNoteRemoved = null, bool exactName = false)
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
            using (FileStream stream = CreateTemporary(temp, path, forceOwnerOnly: expected is not null))
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
                ReplaceExpected(temp, path, expected, bytes, afterCheck, afterReplace, beforeNoteRemoved, exactName);
            }
            else
            {
                Replace(temp, path);
            }

            replaced = true;
        }
        finally
        {
            // A save over an existing file that stopped part way (a full disk, say) leaves its redo note naming this
            // copy; the next read finishes the save from it, so it is kept. Otherwise nothing needs it.
            if (!replaced && !NamedByOutstandingNote(path, temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static bool NamedByOutstandingNote(string path, string temp)
    {
        try
        {
            string pending = path + PendingSuffix;
            if (!File.Exists(pending) || new FileInfo(pending).Length > MaxNoteBytes)
            {
                return false;
            }

            using var stream = new FileStream(pending, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return Encoding.UTF8.GetString(bytes).Split('\n')[0] == Path.GetFileName(temp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Unknown: keep the copy; a stale one is removed by a later write.
            return true;
        }
    }

    /// <summary>
    /// Creates the temporary file. When a target exists the staged copy can be read and written by the writing user
    /// alone (one explicit allow, shut off from inheritance), so it is never readable more widely than the target,
    /// whatever the target's or the folder's rules are, even after a crash. The target's own descriptor is what the
    /// final file keeps (<see cref="Replace"/>). A first save takes the folder's rules, as any new file does.
    /// </summary>
    private static FileStream CreateTemporary(string temp, string path, bool forceOwnerOnly = false)
    {
        // A save over a file it has read stages owner-only even if that file has just gone missing (moved by a sync):
        // the copy carries the same contents, which must not take a wider folder's rules.
        if (!forceOwnerOnly && !File.Exists(path))
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

    /// <summary>The suffix of Berth's intended save, set beside a file that changed under an interrupted save; never cleaned up.</summary>
    public const string KeptSuffix = ".berth-replaced-";

    /// <summary>
    /// Writes <paramref name="written"/> over <paramref name="path"/> in place, under a lock no other program can
    /// share, and only if the file still holds exactly <paramref name="expected"/>. A redo note taken (and held) first
    /// names the verified temporary copy and the digests of the old and new contents, so a crash at any point is
    /// finished by <see cref="Recover(string, bool)"/>: the save is completed only over the version it had read. A file
    /// that changed, or that another program holds, is never written; while the lock is held, nothing else can read,
    /// write, rename or delete it. Writing in place keeps the file's own permissions and attributes.
    /// </summary>
    private static void ReplaceExpected(string temp, string path, byte[] expected, byte[] written, Action? afterCheck, Action? afterReplace, Action? beforeNoteRemoved, bool exactName)
    {
        string pending = path + PendingSuffix;

        // Delete sharing lets this writer remove its own note while still holding it; a reader asks for exclusive
        // access, which any open handle denies, so it waits for this save rather than act on its note.
        using (var note = new FileStream(pending, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete))
        {
            note.Write(Encoding.UTF8.GetBytes(
                Path.GetFileName(temp) + "\n" + Convert.ToHexString(SHA256.HashData(expected)) + "\n" + Convert.ToHexString(SHA256.HashData(written))));
            note.Flush(flushToDisk: true);

            FileStream target;
            try
            {
                target = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Nothing was written. A file another program holds so that it cannot even be read changed under this
                // save; one that is missing or can be read refused the write, and keeps its own error.
                File.Delete(pending);
                if (error is not FileNotFoundException && error is not DirectoryNotFoundException && !CanRead(path))
                {
                    throw new TargetChangedException(path, error);
                }

                throw;
            }

            using (target)
            {
                if (!IsTheFileItself(target, path, exactName) || !HoldsExactly(target, expected))
                {
                    // Changed, or reached through a link: neither is written.
                    File.Delete(pending);
                    throw new TargetChangedException(path);
                }

                afterCheck?.Invoke();
                WriteInPlace(target, written, afterWrite: afterReplace);
            }

            beforeNoteRemoved?.Invoke();
            File.Delete(pending);
        }

        File.Delete(temp);
    }

    /// <summary>
    /// Writes <paramref name="written"/> over an open file in two single calls, so that whatever moment a process
    /// ends, the file holds either its old contents or the new ones: first the new contents padded with trailing
    /// spaces (whitespace JSON allows) to at least the old length, in one write, so nothing of the old is left
    /// after them; then one trim to the new length. A power loss below the file system can still tear a write.
    /// </summary>
    private static void WriteInPlace(FileStream target, byte[] written, Action? afterWrite)
    {
        int length = (int)Math.Max(target.Length, written.Length);
        byte[] padded = new byte[length];
        written.CopyTo(padded, 0);
        padded.AsSpan(written.Length).Fill((byte)' ');
        target.Position = 0;
        target.Write(padded);
        target.Flush(flushToDisk: true);
        afterWrite?.Invoke();
        target.SetLength(written.Length);
        target.Flush(flushToDisk: true);
    }

    /// <summary>Whether <paramref name="now"/> is <paramref name="written"/>, possibly still padded with trailing spaces.</summary>
    private static bool IsWritten(byte[] now, byte[] written) =>
        now.Length >= written.Length
        && now.AsSpan(0, written.Length).SequenceEqual(written)
        && !now.AsSpan(written.Length).ContainsAnyExcept((byte)' ');
    /// <summary>
    /// Reads <paramref name="path"/> as <see cref="TryRead{T}(string, JsonTypeInfo{T}, out T, out string, out byte[])"/>
    /// does, but only a settled version: an interrupted save is finished first and a save under way is waited for,
    /// and a read that a save overlapped (a note is there now, or the file no longer holds what was read) is made
    /// again, so a proposed save that is then taken back is never returned.
    /// </summary>
    /// <exception cref="IOException">The file did not settle, or an interrupted save cannot be finished.</exception>
    /// <exception cref="UnauthorizedAccessException">An interrupted save may not be finished.</exception>
    /// <param name="exactName">As for <see cref="Recover(string, bool)"/>, for the recovery every attempt runs.</param>
    public static bool TryReadSettled<T>(string path, JsonTypeInfo<T> info, bool exactName, out T? value, out string? reason, out byte[]? read) where T : class =>
        TryReadSettled(path, info, exactName, out value, out reason, out read, afterRecover: null, afterRead: null);

    /// <summary>As the public overload; the two actions let a test interleave a save with the read.</summary>
    internal static bool TryReadSettled<T>(string path, JsonTypeInfo<T> info, bool exactName, out T? value, out string? reason, out byte[]? read, Action? afterRecover, Action? afterRead) where T : class
    {
        DateTime giveUp = DateTime.UtcNow + LiveSaveWait;
        for (int attempt = 0; attempt < SettleAttempts; attempt++)
        {
            Recover(path, exactName, afterNoteRead: null);
            afterRecover?.Invoke();
            bool ok = TryRead(path, info, out value, out reason, out read);
            afterRead?.Invoke();
            if (read is null && reason != Missing && File.Exists(path) && DateTime.UtcNow < giveUp)
            {
                // Held by a save (Berth's own takes it exclusively for a moment) or by another program: wait and
                // read again within the budget, rather than report a passing lock as an unreadable file.
                Thread.Sleep(20);
                attempt--;
                continue;
            }

            bool noteNow = File.Exists(path + PendingSuffix);
            bool unchanged = read is null ? !File.Exists(path) || reason != Missing : HoldsExactlyOrMissing(path, read);
            if (!noteNow && unchanged)
            {
                return ok;
            }
        }

        throw new IOException($"'{path}' kept changing while it was read.");
    }

    private const int SettleAttempts = 5;

    /// <summary>False only for a file that is there but cannot be opened for reading; a missing one is not unreadable.</summary>
    private static bool CanRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HoldsExactlyOrMissing(string path, byte[] bytes)
    {
        try
        {
            return HoldsExactly(path, bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The suffix of the redo note a save over an existing file holds until it has written the file.</summary>
    public const string PendingSuffix = ".berth-pending";

    /// <summary>How long a reader waits for a save under way on the same file to settle.</summary>
    internal static readonly TimeSpan LiveSaveWait = TimeSpan.FromSeconds(2);

    /// <summary>A note holds a file name and two digests; anything longer is not one of Berth's.</summary>
    private const int MaxNoteBytes = 512;

    /// <summary>
    /// Finishes a save over <paramref name="path"/> that ended before it removed its redo note (a crash, Task Manager):
    /// when the file still holds the version the save had read, the save is completed from its verified temporary
    /// copy; when it holds the new version, nothing is left to do; when it holds anything else (it changed after the
    /// save ended), it is left exactly as it is and the intended save is kept beside it. Every reader of a Berth file
    /// calls this first; with no note it does nothing.
    /// </summary>
    /// <exception cref="IOException">The interrupted save cannot be finished now, or another Berth is still saving.</exception>
    /// <exception cref="UnauthorizedAccessException">The interrupted save may not be finished.</exception>
    /// <param name="exactName">True for a store whose rule is the exact name (layouts): a file whose name on disk is
    /// spelled differently from <paramref name="path"/> (another case) is not Berth's file and is never written. False
    /// compares names as Windows does. Every caller decides; there is no default.</param>
    public static void Recover(string path, bool exactName) => Recover(path, exactName, afterNoteRead: null);

    /// <summary>As <see cref="Recover(string, bool)"/>; <paramref name="afterNoteRead"/> lets a test act while recovery holds the note.</summary>
    internal static void Recover(string path, bool exactName, Action? afterNoteRead)
    {
        string pending = path + PendingSuffix;
        if (!File.Exists(pending))
        {
            return;
        }

        DateTime giveUp = DateTime.UtcNow + LiveSaveWait;
        FileStream? held = null;
        while (held is null)
        {
            try
            {
                // Taken exclusively (only deletion shared, so this reader can remove it while holding it) and kept
                // until the note is gone: no second reader can act on it, and no new save can start its own meanwhile.
                held = new FileStream(pending, FileMode.Open, FileAccess.Read, FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                // The save or recovery that held it finished meanwhile.
                return;
            }
            catch (IOException) when (DateTime.UtcNow < giveUp)
            {
                // A save or another recovery holds it: wait for it to finish, so the file is never read half written.
                Thread.Sleep(20);
            }
            catch (IOException error)
            {
                throw new IOException($"'{path}' is still being saved by another Berth.", error);
            }
        }

        using (held)
        {
            string[] note = [];
            if (held.Length <= MaxNoteBytes)
            {
                byte[] bytes = new byte[held.Length];
                held.ReadExactly(bytes);
                note = Encoding.UTF8.GetString(bytes).Split('\n');
            }

            afterNoteRead?.Invoke();

            // Only a note this writer makes is acted on: its own temporary name for this target and two SHA-256
            // digests. Anything else is discarded without touching another file.
            string target = Path.GetFileName(path);
            bool Ours(string name) => name.StartsWith(target + ".tmp-", StringComparison.Ordinal) && TemporarySuffix().IsMatch(name[(target.Length + ".tmp-".Length)..]);
            if (note.Length == 3 && Ours(note[0]) && Digest().IsMatch(note[1]) && Digest().IsMatch(note[2]))
            {
                Finish(path, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, note[0]), oldDigest: note[1], newDigest: note[2], exactName);
            }

            File.Delete(pending);
        }
    }

    private static void Finish(string path, string newCopy, string oldDigest, string newDigest, bool exactName)
    {
        // The copy is read once, within the bound, and used only as the bytes whose digest was checked.
        byte[]? written = ReadCopy(newCopy, newDigest);
        FileStream? target = null;
        DateTime giveUp = DateTime.UtcNow + LiveSaveWait;
        while (target is null)
        {
            try
            {
                target = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                // The file went away after the save ended: create nothing, keep what the save meant to write beside it.
                Keep(path, newCopy, written);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // The file refuses writing now (made read-only, or its rules changed): the save can never finish over
                // it. Leave it as it is, so it still loads, and keep what the save meant to write beside it.
                Keep(path, newCopy, written);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < giveUp)
            {
                // Another program (a sync client) has it open for a moment: wait within the budget, as readers do.
                Thread.Sleep(20);
            }
        }

        using (target)
        {
            if (!IsTheFileItself(target, path, exactName))
            {
                // A link now stands where the file was: write nothing through it, keep the intended save beside it.
                Keep(path, newCopy, written);
                return;
            }

            byte[]? now = target.Length <= MaxBytes ? new byte[target.Length] : null;
            if (now is not null)
            {
                target.ReadExactly(now);
            }

            if (now is not null && written is not null && IsWritten(now, written))
            {
                // The save had written its contents; finish trimming them.
                target.SetLength(written.Length);
                target.Flush(flushToDisk: true);
            }
            else if (now is not null && written is not null && Convert.ToHexString(SHA256.HashData(now)).Equals(oldDigest, StringComparison.OrdinalIgnoreCase))
            {
                // Still exactly the version the save had read: complete the save.
                WriteInPlace(target, written, afterWrite: null);
            }
            else
            {
                // It changed after the save ended: leave it, and keep what the save meant to write beside it.
                Keep(path, newCopy, written);
                return;
            }
        }

        Delete(newCopy);
    }
    private static byte[]? ReadCopy(string file, string digest)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaxBytes)
            {
                return null;
            }

            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest, StringComparison.OrdinalIgnoreCase) ? bytes : null;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void Keep(string path, string newCopy, byte[]? written)
    {
        if (written is not null)
        {
            File.Move(newCopy, path + KeptSuffix + Guid.NewGuid().ToString("N"));
        }
        else
        {
            Delete(newCopy);
        }
    }

    private static void Delete(string file)
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }
    /// <summary>
    /// Whether <paramref name="opened"/> is the file named <paramref name="path"/> in its own folder, not something a
    /// link (on the file or on the way to it) led to: the handle's final path must be the folder's final path and the
    /// file's name. Comparing with the folder's own final path keeps a substituted drive letter from counting as a link.
    /// Names compare as Windows compares them, unless <paramref name="exactName"/> asks for the name exactly as given
    /// (the layout store's rule), so a file renamed to another case is not taken for the caller's file.
    /// </summary>
    internal static bool IsTheFileItself(FileStream opened, string path, bool exactName = false)
    {
        string full = Path.GetFullPath(path);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        string final = FinalPath(opened.SafeFileHandle);
        return string.Equals(Path.GetDirectoryName(final), FinalFolderPath(Path.GetDirectoryName(full)!), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(final), Path.GetFileName(full), exactName ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The path Windows resolves the existing folder <paramref name="folder"/> to: one spelling for every way of naming
    /// it (an extended <c>\\?\</c> prefix, a short 8.3 name, a junction or a substituted drive on the way).
    /// </summary>
    /// <exception cref="IOException">The folder cannot be opened.</exception>
    internal static string FinalFolderPath(string folder)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = NativeMethods.CreateFile(
            folder, NativeMethods.FileReadAttributes, NativeMethods.ShareAll, IntPtr.Zero, NativeMethods.OpenExisting, NativeMethods.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException($"The folder '{folder}' could not be opened.", System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        return FinalPath(handle);
    }

    private static string FinalPath(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        char[] buffer = new char[1024];
        uint length = NativeMethods.GetFinalPathNameByHandle(handle, ref buffer[0], (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
        {
            throw new IOException("The file system could not say where a file is.");
        }

        string final = new(buffer, 0, (int)length);
        return final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + final[8..] : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    private static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Whether <paramref name="stream"/>, from its start, holds exactly <paramref name="bytes"/>; read only when the lengths agree.</summary>
    private static bool HoldsExactly(FileStream stream, byte[] bytes)
    {
        if (stream.Length != bytes.Length)
        {
            return false;
        }

        stream.Position = 0;
        byte[] actual = new byte[bytes.Length];
        stream.ReadExactly(actual);
        return actual.AsSpan().SequenceEqual(bytes);
    }
    /// <summary>Whether <paramref name="path"/> holds exactly <paramref name="bytes"/>, read only when the lengths agree.</summary>
    private static bool HoldsExactly(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
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

    [GeneratedRegex(@"^[0-9A-F]{64}\z")]
    private static partial Regex Digest();

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
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
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

    public TargetChangedException(string path, Exception innerException)
        : base($"'{path}' could not be checked just before it was replaced, so it was left as it is.", innerException)
    {
    }
}

/// <summary>The one call the file writer makes that .NET does not wrap.</summary>
internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    internal static partial uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, ref char path, uint size, uint flags);

    internal const uint FileReadAttributes = 0x80;
    internal const uint ShareAll = 0x7;
    internal const uint OpenExisting = 3;
    internal const uint BackupSemantics = 0x02000000;

    /// <summary>CreateFileW, for a folder handle (BACKUP_SEMANTICS), which .NET does not open.</summary>
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    internal static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}