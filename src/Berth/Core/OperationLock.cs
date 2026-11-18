using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Berth.Core;

/// <summary>
/// Save, restore, undo and revert one at a time across the tray app and the command line (SPEC §15): a named mutex
/// per data root, shared across the user's logon sessions, waited for up to <see cref="Wait"/>.
/// </summary>
internal sealed class OperationLock
{
    /// <summary>How long the app and the command line wait for another operation (SPEC §15: 5 seconds).</summary>
    public static readonly TimeSpan AppWait = TimeSpan.FromSeconds(5);

    public OperationLock(BerthPaths paths, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);
        MutexName = NameFor(paths.DataRoot);
        Wait = wait;
    }

    /// <summary>The lock the tray app and the command line use: <see cref="AppWait"/>.</summary>
    public static OperationLock ForApp(BerthPaths paths) => new(paths, AppWait);

    /// <summary>The mutex's name: <c>Global\Berth.Operation.&lt;hash&gt;</c>.</summary>
    public string MutexName { get; }

    public TimeSpan Wait { get; }

    /// <summary>
    /// <c>Global\</c>, so a command line in another of the user's sessions shares it with the tray app, and a hash of
    /// the data root (the first 16 lowercase hex of SHA-256 of the UTF-8 of its full path without trailing
    /// separators, lowercased), so every spelling of one root shares a lock and different roots do not.
    /// </summary>
    internal static string NameFor(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        string root = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        return @"Global\Berth.Operation." + hash;
    }

    /// <summary>
    /// Runs <paramref name="work"/> holding the lock, released on this thread when it returns or throws; returns
    /// <paramref name="busy"/>'s result, without calling <paramref name="work"/>, when the lock is not free within
    /// <see cref="Wait"/>. A lock whose holder ended without releasing it (a crash) counts as free.
    /// </summary>
    public T Run<T>(Func<T> work, Func<T> busy)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(busy);
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(Wait);
        }
        catch (AbandonedMutexException)
        {
            // The last holder ended without releasing it; this thread owns it now.
            acquired = true;
        }

        if (!acquired)
        {
            return busy();
        }

        try
        {
            return work();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
