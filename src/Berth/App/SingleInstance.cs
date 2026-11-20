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
    private readonly Lock _lifecycleLock = new();
    private readonly Lock _eventLock = new();

    private Action? _activated;
    private bool _hasPendingActivation;
    private bool _claimed;
    private bool _disposed;
    private ManualResetEventSlim? _releaseMutex;
    private Thread? _mutexThread;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

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

    /// <summary>Test hook raised after an activation arrives with no subscriber attached and is stored as pending.</summary>
    internal event Action? PendingActivationRecorded;

    /// <summary>
    /// Raised on a thread-pool worker when a second instance sends <c>activate</c>. If an activation arrived before
    /// the first subscriber attached, that subscriber is invoked once on a thread-pool worker on subscription.
    /// Subscribers must marshal UI work to their own dispatcher; exceptions in subscribers never stop delivery.
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
            lock (_eventLock)
            {
                _activated += value;
                replayPending = _hasPendingActivation;
                _hasPendingActivation = false;
            }

            if (replayPending)
            {
                DispatchCallback(value);
            }
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_eventLock)
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
    /// Attempts to claim the per-session named mutex and bind the activation pipe listener.
    /// Uses a dedicated background thread to own the mutex so two <see cref="SingleInstance"/> objects on the same
    /// thread never recursively re-acquire it and <see cref="Dispose"/> always releases it on its owning thread.
    /// Serialized with <see cref="Dispose"/> so a claim never outlives disposal.
    /// </summary>
    public bool TryClaim()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_claimed)
            {
                return true;
            }

            var releaseMutex = new ManualResetEventSlim(initialState: false);
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
                        releaseMutex.Wait();
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
                        mutex.ReleaseMutex();
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
                releaseMutex.Set();
                ownerThread.Join();
                releaseMutex.Dispose();
                throw new IOException($"Failed to claim single-instance mutex '{MutexName}': {claimError.Message}", claimError);
            }

            if (!acquired)
            {
                ownerThread.Join();
                releaseMutex.Dispose();
                return false;
            }

            // Bind the initial pipe server synchronously BEFORE reporting a successful claim so a bind failure
            // immediately releases mutex ownership instead of leaving a dead primary holding the mutex.
            NamedPipeServerStream initialServer;
            try
            {
                initialServer = CreatePipeServer();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                releaseMutex.Set();
                ownerThread.Join();
                releaseMutex.Dispose();
                throw new IOException($"Failed to bind single-instance pipe '{PipeName}': {ex.Message}", ex);
            }

            var cts = new CancellationTokenSource();
            _claimed = true;
            _releaseMutex = releaseMutex;
            _mutexThread = ownerThread;
            _listenerCts = cts;
            _listenerTask = Task.Run(() => ListenLoopAsync(initialServer, cts.Token));
            return true;
        }
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
        ManualResetEventSlim? releaseMutex;

        lock (_lifecycleLock)
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
            releaseMutex = _releaseMutex;
            _listenerCts = null;
            _listenerTask = null;
            _mutexThread = null;
            _releaseMutex = null;
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

        if (releaseMutex is not null)
        {
            releaseMutex.Set();
            mutexThread?.Join();
            releaseMutex.Dispose();
        }
    }

    private NamedPipeServerStream CreatePipeServer() =>
        new(
            PipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

    private async Task ListenLoopAsync(NamedPipeServerStream initialServer, CancellationToken shutdownToken)
    {
        NamedPipeServerStream? nextServer = initialServer;
        while (!shutdownToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            if (nextServer is not null)
            {
                server = nextServer;
                nextServer = null;
            }
            else
            {
                try
                {
                    server = CreatePipeServer();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (shutdownToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(50, shutdownToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }
            }

            using (server)
            {
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

        nextServer?.Dispose();
    }

    private async Task<bool> ReadActivateCommandAsync(NamedPipeServerStream server, CancellationToken shutdownToken)
    {
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        clientCts.CancelAfter(_clientReadTimeout);

        // Strictly bounded to MaxCommandBytes (64 bytes); reject as soon as a 9th byte arrives.
        byte[] buffer = new byte[MaxCommandBytes];
        int totalRead = 0;

        try
        {
            while (totalRead < MaxCommandBytes)
            {
                int remaining = MaxCommandBytes - totalRead;
                int bytesRead = await server.ReadAsync(buffer.AsMemory(totalRead, remaining), clientCts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
                if (totalRead > ActivateCommand.Length)
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
        lock (_eventLock)
        {
            if (_disposed)
            {
                return;
            }

            handler = _activated;
            if (handler is null)
            {
                _hasPendingActivation = true;
                PendingActivationRecorded?.Invoke();
                return;
            }
        }

        DispatchCallback(handler);
    }

    private static void DispatchCallback(Action handler)
    {
        _ = Task.Run(() =>
        {
            foreach (Delegate target in handler.GetInvocationList())
            {
                try
                {
                    ((Action)target)();
                }
                catch (Exception)
                {
                    // A failing subscriber must never fault the listener or prevent later activations.
                }
            }
        });
    }
}
