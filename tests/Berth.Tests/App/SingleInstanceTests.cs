using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
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

        using var recordedPending = new ManualResetEventSlim(false);
        first.PendingActivationRecorded += () => recordedPending.Set();

        Assert.True(first.TryClaim());
        Assert.False(second.TryClaim());

        // Signal BEFORE any subscriber attaches, and wait deterministically for the server to store it as pending.
        Assert.True(second.SignalFirst());
        Assert.True(recordedPending.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Server did not record pending activation.");

        using var delivered = new ManualResetEventSlim(false);
        int count = 0;
        first.Activated += () =>
        {
            Interlocked.Increment(ref count);
            delivered.Set();
        };

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Pending activation was not replayed to subscriber.");
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public void BerthProcessReplacesMissingOrBlankInstanceOverrideAndPreservesExplicitName()
    {
        var missing = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Berth.Tests.Cli.BerthProcess.ApplyInstanceIsolation(missing);
        Assert.StartsWith("test-", missing["BERTH_INSTANCE"], StringComparison.Ordinal);

        var empty = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["BERTH_INSTANCE"] = string.Empty };
        Berth.Tests.Cli.BerthProcess.ApplyInstanceIsolation(empty);
        Assert.StartsWith("test-", empty["BERTH_INSTANCE"], StringComparison.Ordinal);

        var whitespace = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["berth_instance"] = "   \t  " };
        Berth.Tests.Cli.BerthProcess.ApplyInstanceIsolation(whitespace);
        Assert.StartsWith("test-", whitespace["BERTH_INSTANCE"], StringComparison.Ordinal);

        var paired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["BERTH_INSTANCE"] = "paired-test-id" };
        Berth.Tests.Cli.BerthProcess.ApplyInstanceIsolation(paired);
        Assert.Equal("paired-test-id", paired["BERTH_INSTANCE"]);
    }

    [Fact]
    public void FailedPipeBindReleasesMutexImmediatelyBeforeDisposalSoReplacementCanClaim()
    {
        string name = UniqueName();
        int sessionId = Process.GetCurrentProcess().SessionId;
        string pipeName = $"Berth.App.{name}.{sessionId}";

        var failed = new SingleInstance(name, sessionId: sessionId);
        try
        {
            // Reserve the single-instance pipe endpoint with maxNumberOfServerInstances: 1 before TryClaim.
            using (var occupied = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous))
            {
                Assert.Throws<IOException>(() => failed.TryClaim());
            }

            // Keep 'failed' alive (NOT yet disposed) while the replacement claims both mutex and pipe.
            using var replacement = new SingleInstance(name, sessionId: sessionId);
            Assert.True(replacement.TryClaim());
        }
        finally
        {
            failed.Dispose();
        }
    }

    [Fact]
    public void ConcurrentDisposeDuringClaimStartupLeavesNameAndPipeAvailableToReplacement()
    {
        string name = UniqueName();
        var instance = new SingleInstance(name);
        using var enteringDisposeLock = new ManualResetEventSlim(false);
        using var disposeFinished = new ManualResetEventSlim(false);
        instance.BeforeDisposeLock = () => enteringDisposeLock.Set();
        Thread? disposeThread = null;

        bool claimed = instance.TryClaim(beforePipeBind: () =>
        {
            // Launch Dispose() on a dedicated thread while TryClaim() holds _lifecycleLock.
            disposeThread = new Thread(() =>
            {
                instance.Dispose();
                disposeFinished.Set();
            })
            {
                IsBackground = true,
            };
            disposeThread.Start();

            Assert.True(enteringDisposeLock.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            // Wait deterministically until disposeThread is actually blocked waiting on _lifecycleLock.
            DateTime waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while ((disposeThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0 && DateTime.UtcNow < waitDeadline)
            {
                Thread.Yield();
            }

            Assert.True(
                (disposeThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                "Dispose thread did not block on _lifecycleLock while TryClaim held it.");
            Assert.False(disposeFinished.IsSet, "Dispose must not complete while TryClaim holds _lifecycleLock.");
        });

        Assert.True(claimed);
        Assert.NotNull(disposeThread);
        Assert.True(disposeFinished.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Concurrent Dispose() did not finish cleanly.");
        disposeThread.Join();

        // After serialized Dispose() finishes, a replacement must claim both mutex and pipe and receive activations.
        using var replacement = new SingleInstance(name);
        Assert.True(replacement.TryClaim());
        using var activated = new ManualResetEventSlim(false);
        replacement.Activated += () => activated.Set();

        using var caller = new SingleInstance(name);
        Assert.True(caller.SignalFirst());
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ThrowingOrDisposingSubscriberDoesNotStopDeliveryOrDeadlock()
    {
        string name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.TryClaim());

        using var secondDelivered = new ManualResetEventSlim(false);
        int callCount = 0;
        first.Activated += () =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                throw new InvalidOperationException("Simulated subscriber failure.");
            }

            secondDelivered.Set();
        };

        using var caller = new SingleInstance(name);
        Assert.True(caller.SignalFirst());
        Assert.True(caller.SignalFirst());
        Assert.True(secondDelivered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Listener stopped delivering after a subscriber threw.");
        Assert.Equal(2, Volatile.Read(ref callCount));

        // Subscriber calling Dispose() inside Activated must not deadlock.
        string disposeName = UniqueName();
        var selfDisposing = new SingleInstance(disposeName);
        Assert.True(selfDisposing.TryClaim());
        using var disposedInCallback = new ManualResetEventSlim(false);
        selfDisposing.Activated += () =>
        {
            selfDisposing.Dispose();
            disposedInCallback.Set();
        };

        using var trigger = new SingleInstance(disposeName);
        Assert.True(trigger.SignalFirst());
        Assert.True(disposedInCallback.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Disposing inside Activated deadlocked.");

        // Confirm mutex and pipe are free for a replacement after self-disposal.
        using var replacement = new SingleInstance(disposeName);
        Assert.True(replacement.TryClaim());
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

        // 5. Client that holds pipe open WITHOUT closing must be evicted by server's clientReadTimeout,
        // allowing a separate valid fragmented client to connect and trigger Activated while slowClient is STILL open!
        using (var slowClient = new NamedPipeClientStream(".", expectedPipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            slowClient.Connect(1000);
            slowClient.Write("acti"u8);
            slowClient.Flush();

            // Keep slowClient open while a separate valid fragmented client connects and succeeds after the 250ms timeout.
            using (var fragmentedClient = new NamedPipeClientStream(".", expectedPipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
            {
                fragmentedClient.Connect(2000);
                fragmentedClient.Write("acti"u8);
                fragmentedClient.Flush();
                fragmentedClient.Write("vate"u8);
                fragmentedClient.Flush();
            }

            Assert.True(validOne.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "Fragmented valid 'activate' did not raise Activated while slow client stayed open.");
            Assert.Equal(1, Volatile.Read(ref count));
        }

        // 6. Follow with a normal SignalFirst() and verify exact count = 2.
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
            var sw = Stopwatch.StartNew();
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
            // Server may close the pipe early once the 8-byte command length is exceeded.
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
