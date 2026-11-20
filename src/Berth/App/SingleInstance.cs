using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Berth.App;

/// <summary>Outcome of a secondary instance attempting to hand off to or take over from an existing instance (SPEC §17).</summary>
internal enum SecondaryOutcome
{
    /// <summary>The running primary instance was signalled; this process should exit 0.</summary>
    ActivatedExisting,

    /// <summary>The primary did not answer within the connect timeout and this instance claimed the mutex on its single retry; continue primary startup.</summary>
    ClaimedPrimary,

    /// <summary>The primary did not answer and the single reclaim attempt also failed; this process should exit 3 having started nothing.</summary>
    Refused,
}

/// <summary>
/// Ensures one tray app runs per user and logon session, and forwards a second launch's activation request
/// to the running instance over a current-user-only named pipe (SPEC §17).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const int MaxCommandBytes = 64;
    private static readonly byte[] ActivateCommand = Encoding.ASCII.GetBytes("activate");
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultClientReadTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _clientReadTimeout;
    private readonly Lock _stateLock = new();
    private readonly ManualResetEventSlim _releaseMutex = new(initialState: false);

    private Action? _activated;
    private bool _hasPendingActivation;
    private bool _claimed;
    private bool _disposed;
    private Thread? _mutexThread;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private ManualResetEventSlim? _listenerReady;

    public SingleInstance(
        string name,
        int? sessionId = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? clientReadTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        int effectiveSessionId = sessionId ?? Process.GetCurrentProcess().SessionId;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _clientReadTimeout = clientReadTimeout ?? DefaultClientReadTimeout;
        if (_connectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout), "Connect timeout must be positive.");
        }

        if (_clientReadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(clientReadTimeout), "Client read timeout must be positive.");
        }

        Name = name;
        SessionId = effectiveSessionId;
        MutexName = $@"Local\Berth.App.{name}";
        PipeName = $"Berth.App.{name}.{effectiveSessionId}";
    }

    public string Name { get; }

    public int SessionId { get; }

    internal string MutexName { get; }

    internal string PipeName { get; }

    /// <summary>
    /// Raised when a second instance sends <c>activate</c>. If an activation arrived before the first
    /// subscriber attached, that subscriber is invoked once immediately on subscription.
    /// </summary>
    public event Action? Activated
    {
        add
        {
            if (value is null)
            {
                return;
            }

            bool replayPending;
            lock (_stateLock)
            {
                _activated += value;
                replayPending = _hasPendingActivation;
                _hasPendingActivation = false;
            }

            if (replayPending)
            {
                value();
            }
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_stateLock)
            {
                _activated -= value;
            }
        }
    }

    /// <summary>
    /// Resolves the instance name from <c>BERTH_INSTANCE</c> when non-blank, otherwise the current user's Windows SID.
    /// </summary>
    public static string ResolveName(Func<string, string?>? getenv = null)
    {
        Func<string, string?> readEnv = getenv ?? Environment.GetEnvironmentVariable;
        string? overrideName = readEnv("BERTH_INSTANCE");
        if (!string.IsNullOrWhiteSpace(overrideName))
        {
            return overrideName.Trim();
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        return !string.IsNullOrWhiteSpace(sid) ? sid : Environment.UserName;
    }

    /// <summary>
    /// Attempts to claim the per-session named mutex and start the activation pipe listener.
    /// Uses a dedicated background thread to own the mutex so two <see cref="SingleInstance"/> objects on the same
    /// thread never recursively re-acquire it and <see cref="Dispose"/> always releases it on its owning thread.
    /// </summary>
    public bool TryClaim()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateLock)
        {
            if (_claimed)
            {
                return true;
            }
        }

        using var claimDone = new ManualResetEventSlim(initialState: false);
        bool acquired = false;
        Exception? claimError = null;

        var ownerThread = new Thread(() =>
        {
            Mutex? mutex = null;
            bool owns = false;
            try
            {
                mutex = new Mutex(initiallyOwned: false, MutexName);
                try
                {
                    owns = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner exited without releasing while another handle kept the named mutex alive.
                    owns = true;
                }

                acquired = owns;
                claimDone.Set();

                if (owns)
                {
                    _releaseMutex.Wait();
                }
            }
            catch (Exception ex)
            {
                claimError = ex;
                claimDone.Set();
            }
            finally
            {
                if (owns && mutex is not null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Ignore if somehow no longer owned on teardown.
                    }
                }

                mutex?.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = $"Berth.SingleInstance.Mutex.{Name}",
        };

        ownerThread.Start();
        claimDone.Wait();

        if (claimError is not null)
        {
            ownerThread.Join();
            throw new IOException($"Failed to claim single-instance mutex '{MutexName}': {claimError.Message}", claimError);
        }

        if (!acquired)
        {
            ownerThread.Join();
            return false;
        }

        var cts = new CancellationTokenSource();
        var ready = new ManualResetEventSlim(initialState: false);

        lock (_stateLock)
        {
            _claimed = true;
            _mutexThread = ownerThread;
            _listenerCts = cts;
            _listenerReady = ready;
            _listenerTask = Task.Run(() => ListenLoopAsync(ready, cts.Token));
        }

        ready.Wait();
        return true;
    }

    /// <summary>
    /// Connects to the running primary instance's named pipe within <c>connectTimeout</c>, writes <c>activate</c>,
    /// and flushes. Returns <see langword="true"/> only when the write and flush complete.
    /// </summary>
    public bool SignalFirst()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

            int timeoutMs = (int)Math.Clamp(Math.Ceiling(_connectTimeout.TotalMilliseconds), 1, int.MaxValue);
            client.Connect(timeoutMs);
            client.Write(ActivateCommand, 0, ActivateCommand.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Executes the secondary-instance handoff after an initial <see cref="TryClaim"/> returned <see langword="false"/>:
    /// tries to signal the primary within <c>connectTimeout</c>; if the primary does not answer, tries once to claim
    /// the mutex, returning <see cref="SecondaryOutcome.ClaimedPrimary"/> on success or <see cref="SecondaryOutcome.Refused"/> otherwise.
    /// </summary>
    public SecondaryOutcome RunSecondary() => RunSecondary(onBeforeReclaim: null);

    internal SecondaryOutcome RunSecondary(Action? onBeforeReclaim)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SignalFirst())
        {
            return SecondaryOutcome.ActivatedExisting;
        }

        onBeforeReclaim?.Invoke();
        return TryClaim() ? SecondaryOutcome.ClaimedPrimary : SecondaryOutcome.Refused;
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? listenerTask;
        Thread? mutexThread;
        ManualResetEventSlim? ready;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _claimed = false;
            cts = _listenerCts;
            listenerTask = _listenerTask;
            mutexThread = _mutexThread;
            ready = _listenerReady;
            _listenerCts = null;
            _listenerTask = null;
            _mutexThread = null;
            _listenerReady = null;
        }

        cts?.Cancel();

        if (listenerTask is not null)
        {
            try
            {
                listenerTask.Wait();
            }
            catch (AggregateException)
            {
                // Cancellation or pipe shutdown errors during teardown are expected.
            }
        }

        cts?.Dispose();
        ready?.Dispose();

        _releaseMutex.Set();
        mutexThread?.Join();
        _releaseMutex.Dispose();
    }

    private async Task ListenLoopAsync(ManualResetEventSlim ready, CancellationToken shutdownToken)
    {
        bool signalledReady = false;
        try
        {
            while (!shutdownToken.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

                if (!signalledReady)
                {
                    signalledReady = true;
                    ready.Set();
                }

                try
                {
                    await server.WaitForConnectionAsync(shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    continue;
                }

                bool valid = await ReadActivateCommandAsync(server, shutdownToken).ConfigureAwait(false);
                if (valid && !shutdownToken.IsCancellationRequested)
                {
                    RaiseActivated();
                }
            }
        }
        finally
        {
            if (!signalledReady)
            {
                ready.Set();
            }
        }
    }

    private async Task<bool> ReadActivateCommandAsync(NamedPipeServerStream server, CancellationToken shutdownToken)
    {
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        clientCts.CancelAfter(_clientReadTimeout);

        byte[] buffer = new byte[MaxCommandBytes + 1];
        int totalRead = 0;

        try
        {
            while (totalRead <= MaxCommandBytes)
            {
                int remaining = buffer.Length - totalRead;
                int bytesRead = await server.ReadAsync(buffer.AsMemory(totalRead, remaining), clientCts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
                if (totalRead > MaxCommandBytes)
                {
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return false;
        }

        return totalRead == ActivateCommand.Length
            && buffer.AsSpan(0, ActivateCommand.Length).SequenceEqual(ActivateCommand);
    }

    private void RaiseActivated()
    {
        Action? handler;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            handler = _activated;
            if (handler is null)
            {
                _hasPendingActivation = true;
                return;
            }
        }

        handler();
    }
}
