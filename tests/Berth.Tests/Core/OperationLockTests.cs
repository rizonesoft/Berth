using System.Diagnostics;
using Berth.Core;

namespace Berth.Tests.Core;

/// <summary>Holds an operation lock's mutex on its own thread until told to let go, signalled with events.</summary>
internal sealed class MutexHolder : IDisposable
{
    private readonly ManualResetEventSlim held = new();
    private readonly ManualResetEventSlim release = new();
    private readonly Thread thread;

    public MutexHolder(string name, bool abandon = false)
    {
        thread = new Thread(() =>
        {
            var mutex = new Mutex(initiallyOwned: false, name);
            mutex.WaitOne();
            held.Set();
            release.Wait();
            if (!abandon)
            {
                mutex.ReleaseMutex();
            }

            // An abandoning holder ends its thread still owning the mutex, as a crash would.
            mutex.Dispose();
        })
        { IsBackground = true };
        thread.Start();
        if (!held.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The holder did not take the mutex.");
        }
    }

    public void Release() => release.Set();

    public void Join()
    {
        Release();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The holder thread did not end.");
        }
    }

    public void Dispose()
    {
        Join();
        held.Dispose();
        release.Dispose();
    }
}

public sealed class OperationLockTests
{
    private static OperationLock Lock(TestData data, int waitMs) => new(BerthPaths.ForRoot(data.Root), TimeSpan.FromMilliseconds(waitMs));

    [Fact]
    public void ABusyLockReturnsBusyWithoutRunningTheWorkThenRunsItOnceFree()
    {
        using var data = new TestData();
        OperationLock gate = Lock(data, 200);
        int calls = 0;

        using (new MutexHolder(gate.MutexName))
        {
            Assert.Equal("busy", gate.Run(() => { calls++; return "work"; }, () => "busy"));
            Assert.Equal(0, calls);
        }

        Assert.Equal("work", gate.Run(() => { calls++; return "work"; }, () => "busy"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AWaitingRunTakesTheLockWhenItIsReleasedBeforeTheDeadline()
    {
        using var data = new TestData();
        OperationLock gate = Lock(data, 10_000);
        using var holder = new MutexHolder(gate.MutexName);
        int calls = 0, busies = 0;
        CancellationToken cancel = TestContext.Current.CancellationToken;

        string? result = null;
        var contender = new Thread(() => result = gate.Run(() => { calls++; return "work"; }, () => { busies++; return "busy"; }));
        contender.Start();

        // Released only once the run is seen blocked waiting for the mutex (a run that does not wait is never seen
        // blocked); the run must then take the lock within its 10-second deadline.
        Assert.True(SeenBlocked(contender, TimeSpan.FromSeconds(10)), "the run was never seen waiting for the lock");
        holder.Release();
        Task<bool> ended = Task.Run(() => contender.Join(TimeSpan.FromSeconds(10)), cancel);
        Assert.True(await ended);
        Assert.Equal("work", result);

        Assert.Equal((1, 0), (calls, busies));
    }

    [Fact]
    public async Task ATimedOutRunIsBusyOnceAndOnlyAfterTheWait()
    {
        using var data = new TestData();
        OperationLock gate = Lock(data, 400);
        using var holder = new MutexHolder(gate.MutexName);
        int busies = 0;

        var clock = Stopwatch.StartNew();
        Task<string> run = Task.Run(() => gate.Run(() => "work", () => { busies++; return "busy"; }), TestContext.Current.CancellationToken);

        // Bounded from outside, so a wait far longer than the configured one fails rather than hangs.
        Assert.True(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)) == run, "the run did not time out");
        clock.Stop();

        Assert.Equal("busy", await run);
        Assert.Equal(1, busies);
        Assert.InRange(clock.ElapsedMilliseconds, 350, 2000);
    }

    [Fact]
    public void TheLockIsHeldWhileTheWorkRunsAndFreeAfterIt()
    {
        using var data = new TestData();
        OperationLock gate = Lock(data, 0);

        string during = gate.Run(() => Probe(gate.MutexName), () => throw new InvalidOperationException("busy"));

        Assert.Equal("held", during);
        Assert.Equal("free", Probe(gate.MutexName));
    }

    [Fact]
    public void WorkThatThrowsReleasesTheLockAtOnce()
    {
        using var data = new TestData();
        OperationLock gate = Lock(data, 0);

        // Another handle stays open, as another waiting process's would, so an unreleased mutex stays owned.
        using var other = new Mutex(initiallyOwned: false, gate.MutexName);
        Assert.Throws<InvalidDataException>(() => gate.Run<int>(() => throw new InvalidDataException("boom"), () => 0));

        // Released, not abandoned by a handle closed while still owned.
        Assert.Equal("free", Probe(gate.MutexName));
    }

    [Fact]
    public void AnAbandonedLockCountsAsFree()
    {
        using var data = new TestData();
        OperationLock gate = Lock(data, 1000);

        // A second handle keeps the mutex alive across the holder's end, so it is really abandoned.
        using var keep = new Mutex(initiallyOwned: false, gate.MutexName);
        new MutexHolder(gate.MutexName, abandon: true).Join();
        int calls = 0;

        Assert.Equal("work", gate.Run(() => { calls++; return "work"; }, () => "busy"));
        Assert.Equal(1, calls);
        Assert.Equal("free", Probe(gate.MutexName));
    }

    [Fact]
    public void SpellingsOfOneRootShareALockAndOtherRootsDoNot()
    {
        using var data = new TestData();
        using var other = new TestData();
        string root = BerthPaths.ForRoot(data.Root).DataRoot;

        Assert.Equal(OperationLock.NameFor(root), OperationLock.NameFor(root.ToUpperInvariant() + @"\"));
        Assert.Equal(OperationLock.NameFor(root), OperationLock.NameFor(Path.Combine(root, "sub", "..")));
        Assert.Equal(OperationLock.NameFor(root), OperationLock.NameFor(@"\\?\" + root));
        Assert.NotEqual(OperationLock.NameFor(root), OperationLock.NameFor(BerthPaths.ForRoot(other.Root).DataRoot));

        using (new MutexHolder(OperationLock.NameFor(root.ToUpperInvariant())))
        {
            Assert.Equal("busy", Lock(data, 100).Run(() => "work", () => "busy"));
            Assert.Equal("work", Lock(other, 100).Run(() => "work", () => "busy"));
        }
    }

    [Fact]
    public void TheAppWaitsFiveSecondsOnALockSharedAcrossSessions()
    {
        using var data = new TestData();
        OperationLock gate = OperationLock.ForApp(BerthPaths.ForRoot(data.Root));

        Assert.Equal(TimeSpan.FromSeconds(5), gate.Wait);
        Assert.Matches(@"^Global\\Berth\.Operation\.[0-9a-f]{16}\z", gate.MutexName);
    }

    [Fact]
    public void AThreadBlockedOnAnEventIsSeenBlocked()
    {
        using var go = new ManualResetEventSlim();
        var waiter = new Thread(() => go.Wait()) { IsBackground = true };
        waiter.Start();

        Assert.True(SeenBlocked(waiter, TimeSpan.FromSeconds(10)));
        go.Set();
        Assert.True(waiter.Join(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void AThreadThatStaysBusyIsNotSeenBlocked()
    {
        // Alive through the whole deadline and never waiting, as a run that does not wait for the mutex would be
        // if the scheduler held it back: the observation must say no.
        using var stop = new CancellationTokenSource();
        var busy = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                Thread.SpinWait(1000);
            }
        })
        { IsBackground = true };
        busy.Start();

        Assert.False(SeenBlocked(busy, TimeSpan.FromMilliseconds(300)));
        Assert.True(busy.IsAlive);
        stop.Cancel();
        Assert.True(busy.Join(TimeSpan.FromSeconds(10)));
    }

    /// <summary>Whether <paramref name="thread"/> is seen in a wait (blocked) within <paramref name="deadline"/>.</summary>
    private static bool SeenBlocked(Thread thread, TimeSpan deadline)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < deadline && thread.IsAlive)
        {
            if ((thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0)
            {
                return true;
            }

            Thread.Yield();
        }

        return false;
    }

    /// <summary>What another thread finds at once: "free", "abandoned" (its holder closed it still owning it) or
    /// "held". It lets go of anything it takes.</summary>
    private static string Probe(string name)
    {
        string found = "held";
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, name);
            try
            {
                found = mutex.WaitOne(0) ? "free" : "held";
            }
            catch (AbandonedMutexException)
            {
                found = "abandoned";
            }

            if (found != "held")
            {
                mutex.ReleaseMutex();
            }
        });
        thread.Start();
        thread.Join();
        return found;
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class CrossProcessOperationLockTests
{
    [Fact]
    public void AnotherProcessHoldingTheLockMakesRunBusyAndSeesItHeldDuringWork()
    {
        using var data = new TestData();
        OperationLock gate = new(BerthPaths.ForRoot(data.Root), TimeSpan.FromMilliseconds(200));
        int pid;
        using (HelperProcess helper = HelperProcess.Start(data.Root, "mutex", gate.MutexName))
        {
            pid = helper.Id;
            Assert.Equal("acquired", helper.Send("try"));
            Assert.Equal("busy", gate.Run(() => "work", () => "busy"));
            Assert.Equal("released", helper.Send("release"));

            // Now the test process holds it: the helper is refused during the work and takes it after.
            Assert.Equal("busy", gate.Run(() => helper.Send("try"), () => "not acquired"));
            Assert.Equal("acquired", helper.Send("try"));
            Assert.Equal("released", helper.Send("release"));
        }

        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }
}
