using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Berth.App;

namespace Berth.Tests.App;

public sealed class SingleInstanceTests
{
    private static string UniqueName() => "test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void ResolveNameUsesNonBlankOverrideAndFallsBackToCurrentUserSidWithoutClaiming()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string expectedSid = identity.User!.Value;

        Assert.Equal("custom-instance", SingleInstance.ResolveName(k => k == "BERTH_INSTANCE" ? "  custom-instance  " : null));
        Assert.Equal(expectedSid, SingleInstance.ResolveName(_ => null));
        Assert.Equal(expectedSid, SingleInstance.ResolveName(k => k == "BERTH_INSTANCE" ? string.Empty : null));
        Assert.Equal(expectedSid, SingleInstance.ResolveName(k => k == "BERTH_INSTANCE" ? "   \t  " : null));
    }

    [Fact]
    public void FirstClaimSucceedsSecondOnSameThreadFailsAndSignalFirstRaisesActivated()
    {
        string name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        // Even on the exact same thread, a second SingleInstance must NOT recursively claim the mutex.
        Assert.True(first.TryClaim());
        Assert.False(second.TryClaim());

        using var activated = new ManualResetEventSlim(false);
        first.Activated += () => activated.Set();

        Assert.Equal(SecondaryOutcome.ActivatedExisting, second.RunSecondary());
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Primary did not receive activation within 2 seconds.");
    }

    [Fact]
    public void PendingActivationReceivedBeforeSubscriberAttachesIsDeliveredOnceOnSubscribe()
    {
        string name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        Assert.True(first.TryClaim());
        Assert.False(second.TryClaim());

        // Signal BEFORE any subscriber attaches to Activated.
        Assert.True(second.SignalFirst());

        // Wait briefly for the background pipe read to finish, then subscribe and verify delivery once.
        using var delivered = new ManualResetEventSlim(false);
        int count = 0;
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        first.Activated += () =>
        {
            Interlocked.Increment(ref count);
            delivered.Set();
        };

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Pending activation was not delivered to subscriber.");
        Assert.Equal(1, Volatile.Read(ref count));
        Assert.True(DateTime.UtcNow <= deadline + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AbandonedMutexIsClaimedSuccessfullyWhenRetainedHandleKeepsObjectAlive()
    {
        string name = UniqueName();
        string mutexName = $@"Local\Berth.App.{name}";

        // Keep an extra handle open so the named mutex object stays alive after the abandoning thread exits.
        using var retainer = new Mutex(initiallyOwned: false, mutexName);

        var abandoner = new Thread(() =>
        {
            var owned = new Mutex(initiallyOwned: false, mutexName);
            Assert.True(owned.WaitOne(0));
            // Exit thread WITHOUT calling ReleaseMutex() to abandon the mutex.
        });
        abandoner.Start();
        abandoner.Join();

        using var recovered = new SingleInstance(name);
        Assert.True(recovered.TryClaim());
    }

    [Fact]
    public void PipeListenerEnforcesExactFramingBoundedReadAndSessionScopedPipeName()
    {
        string name = UniqueName();
        int customSessionId = 4242;
        using var primary = new SingleInstance(
            name,
            sessionId: customSessionId,
            connectTimeout: TimeSpan.FromMilliseconds(500),
            clientReadTimeout: TimeSpan.FromMilliseconds(250));

        Assert.True(primary.TryClaim());

        // Independently construct the expected session-scoped pipe name (verifying server did not omit sessionId).
        string expectedPipeName = $"Berth.App.{name}.{customSessionId}";
        Assert.Equal(expectedPipeName, primary.PipeName);

        int count = 0;
        using var validOne = new ManualResetEventSlim(false);
        using var validTwo = new ManualResetEventSlim(false);
        primary.Activated += () =>
        {
            int n = Interlocked.Increment(ref count);
            if (n == 1)
            {
                validOne.Set();
            }
            else if (n == 2)
            {
                validTwo.Set();
            }
        };

        // 1. Oversized input (10,000 bytes) must be rejected and listener must stay alive.
        SendRaw(expectedPipeName, new byte[10_000]);

        // 2. Immediate EOF (0 bytes) must be rejected.
        SendRaw(expectedPipeName, []);

        // 3. Valid prefix with trailing data ("activate!") must be rejected.
        SendRaw(expectedPipeName, "activate!"u8.ToArray());

        // 4. Malformed command ("hello") must be rejected.
        SendRaw(expectedPipeName, "hello"u8.ToArray());

        // 5. Client that holds pipe open without sending EOF must time out after clientReadTimeout without hanging listener.
        using (var slowClient = new NamedPipeClientStream(".", expectedPipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            slowClient.Connect(1000);
            slowClient.Write("acti"u8);
            slowClient.Flush();
            // Hold open past the 250ms clientReadTimeout so server aborts this client and returns to accept.
            Thread.Sleep(400);
        }

        // 6. Valid fragmented "activate" across two writes must succeed!
        using (var fragmentedClient = new NamedPipeClientStream(".", expectedPipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            fragmentedClient.Connect(1000);
            fragmentedClient.Write("acti"u8);
            fragmentedClient.Flush();
            fragmentedClient.Write("vate"u8);
            fragmentedClient.Flush();
        }

        Assert.True(validOne.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Fragmented valid 'activate' did not raise Activated.");
        Assert.Equal(1, Volatile.Read(ref count));

        // 7. Follow with a normal SignalFirst() and verify exact count = 2.
        using var secondary = new SingleInstance(name, sessionId: customSessionId);
        Assert.True(secondary.SignalFirst());
        Assert.True(validTwo.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Second valid 'activate' did not raise Activated.");
        Assert.Equal(2, Volatile.Read(ref count));
    }

    [Fact]
    public void RunSecondaryRefusesWhenMutexHeldWithNoPipeAndTakesOverWhenReleasedBeforeSingleRetry()
    {
        // Case A: Mutex held with NO pipe endpoint -> RunSecondary times out connect, tries once to reclaim, returns Refused.
        string refusedName = UniqueName();
        using (var holder = new MutexHolder($@"Local\Berth.App.{refusedName}"))
        {
            using var secondary = new SingleInstance(
                refusedName,
                connectTimeout: TimeSpan.FromMilliseconds(150),
                clientReadTimeout: TimeSpan.FromMilliseconds(150));
            Assert.False(secondary.TryClaim());

            int beforeReclaimCalls = 0;
            SecondaryOutcome outcome = secondary.RunSecondary(onBeforeReclaim: () => beforeReclaimCalls++);
            Assert.Equal(SecondaryOutcome.Refused, outcome);
            Assert.Equal(1, beforeReclaimCalls);
        }

        // Case B: Mutex held with NO pipe endpoint, released right before the single reclaim -> RunSecondary returns ClaimedPrimary.
        string takeoverName = UniqueName();
        var takeoverHolder = new MutexHolder($@"Local\Berth.App.{takeoverName}");
        try
        {
            using var secondary = new SingleInstance(
                takeoverName,
                connectTimeout: TimeSpan.FromMilliseconds(150),
                clientReadTimeout: TimeSpan.FromMilliseconds(150));
            Assert.False(secondary.TryClaim());

            SecondaryOutcome outcome = secondary.RunSecondary(onBeforeReclaim: () => takeoverHolder.Dispose());
            Assert.Equal(SecondaryOutcome.ClaimedPrimary, outcome);

            // Confirm the taken-over instance is now listening on its pipe.
            using var activated = new ManualResetEventSlim(false);
            secondary.Activated += () => activated.Set();
            using var third = new SingleInstance(takeoverName);
            Assert.True(third.SignalFirst());
            Assert.True(activated.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        finally
        {
            takeoverHolder.Dispose();
        }
    }

    [Fact]
    public void DisposeStopsIdleAndActiveListenersAndReplacementReceivesActivationsExclusively()
    {
        // 1. Dispose while idle (waiting for connection).
        string idleName = UniqueName();
        var idleFirst = new SingleInstance(idleName);
        int idleFirstCount = 0;
        idleFirst.Activated += () => Interlocked.Increment(ref idleFirstCount);
        Assert.True(idleFirst.TryClaim());
        idleFirst.Dispose();

        using (var replacement = new SingleInstance(idleName))
        {
            Assert.True(replacement.TryClaim());
            using var gotIt = new ManualResetEventSlim(false);
            replacement.Activated += () => gotIt.Set();

            using var caller = new SingleInstance(idleName);
            Assert.True(caller.SignalFirst());
            Assert.True(gotIt.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.Equal(0, Volatile.Read(ref idleFirstCount));
        }

        // 2. Dispose while a client holds the pipe open mid-read.
        string busyName = UniqueName();
        var busyFirst = new SingleInstance(busyName, clientReadTimeout: TimeSpan.FromSeconds(5));
        int busyFirstCount = 0;
        busyFirst.Activated += () => Interlocked.Increment(ref busyFirstCount);
        Assert.True(busyFirst.TryClaim());

        using (var hangingClient = new NamedPipeClientStream(".", busyFirst.PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            hangingClient.Connect(1000);
            hangingClient.Write("ac"u8);
            hangingClient.Flush();

            // Dispose must cancel the active read promptly without waiting for the 5s clientReadTimeout.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            busyFirst.Dispose();
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Dispose took {sw.Elapsed.TotalMilliseconds:F0} ms while client held pipe open.");
        }

        using var busyReplacement = new SingleInstance(busyName);
        Assert.True(busyReplacement.TryClaim());
        using var replacementGotIt = new ManualResetEventSlim(false);
        busyReplacement.Activated += () => replacementGotIt.Set();

        using var busyCaller = new SingleInstance(busyName);
        Assert.True(busyCaller.SignalFirst());
        Assert.True(replacementGotIt.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(0, Volatile.Read(ref busyFirstCount));
    }

    private static void SendRaw(string pipeName, byte[] payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(1000);
            if (payload.Length > 0)
            {
                client.Write(payload, 0, payload.Length);
                client.Flush();
            }
        }
        catch (IOException)
        {
            // Server may close the pipe early once MaxCommandBytes is exceeded.
        }
    }

    private sealed class MutexHolder : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly Thread _thread;
        private bool _disposed;

        public MutexHolder(string mutexName)
        {
            using var acquired = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                using var mutex = new Mutex(initiallyOwned: false, mutexName);
                Assert.True(mutex.WaitOne(0));
                acquired.Set();
                _release.Wait();
                mutex.ReleaseMutex();
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            acquired.Wait();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _release.Set();
            _thread.Join();
            _release.Dispose();
        }
    }
}
