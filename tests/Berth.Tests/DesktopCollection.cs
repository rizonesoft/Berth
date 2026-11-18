namespace Berth.Tests;

/// <summary>
/// The one collection for tests that touch the real desktop, open a window, add a tray icon or start berth.exe.
/// Its tests never run beside each other, and its fixture holds a machine-wide mutex so that a second test run on
/// the same machine waits for the first instead of clashing with it (AGENTS.md, "Interface tests take turns").
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopCollectionDefinition : ICollectionFixture<DesktopTestGate>
{
    public const string Name = "Desktop";
}

/// <summary>Holds <see cref="DefaultMutexName"/> for as long as the Desktop collection runs.</summary>
public sealed class DesktopTestGate : IDisposable
{
    public const string DefaultMutexName = @"Global\Berth.Tests.Desktop";

    private readonly ManualResetEventSlim release = new();
    private readonly Thread owner;
    private bool disposed;

    public DesktopTestGate()
        : this(DefaultMutexName, TimeSpan.FromMinutes(10))
    {
    }

    /// <summary>Takes <paramref name="mutexName"/>, waiting up to <paramref name="wait"/>, or throws.</summary>
    internal DesktopTestGate(string mutexName, TimeSpan wait)
    {
        MutexName = mutexName;
        using var settled = new ManualResetEventSlim();
        bool acquired = false;
        Exception? failure = null;

        // A mutex belongs to the thread that took it, and xUnit does not promise which thread disposes a fixture,
        // so one thread of the gate's own takes the mutex, holds it, and releases it when Dispose says so.
        owner = new Thread(() =>
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: false, mutexName);
                try
                {
                    acquired = mutex.WaitOne(wait);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
            }
            catch (Exception error)
            {
                failure = error;
            }

            settled.Set();
            if (acquired)
            {
                release.Wait();
                mutex!.ReleaseMutex();
            }

            mutex?.Dispose();
        })
        {
            IsBackground = true,
            Name = "DesktopTestGate",
        };
        owner.Start();
        settled.Wait();

        if (failure is not null)
        {
            throw new InvalidOperationException($"The test gate could not open {mutexName}.", failure);
        }

        if (!acquired)
        {
            owner.Join();
            throw new TimeoutException($@"another Berth interface test run holds {mutexName}");
        }
    }

    public string MutexName { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        release.Set();
        owner.Join();
        release.Dispose();
    }
}
