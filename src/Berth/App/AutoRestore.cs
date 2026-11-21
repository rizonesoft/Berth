using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>One-shot timer handle created by <see cref="ITimerFactory"/>.</summary>
internal interface IOneShotTimer : IDisposable
{
    void Cancel();
}

/// <summary>Creates cancellable one-shot timers so <see cref="AutoRestore"/> can be tested deterministically without real sleeps.</summary>
internal interface ITimerFactory
{
    IOneShotTimer Create(TimeSpan dueTime, Action callback);
}

/// <summary>Production <see cref="ITimerFactory"/> backed by <see cref="System.Threading.Timer"/>.</summary>
internal sealed class SystemTimerFactory : ITimerFactory
{
    public IOneShotTimer Create(TimeSpan dueTime, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new SystemOneShotTimer(dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime, callback);
    }

    private sealed class SystemOneShotTimer : IOneShotTimer
    {
        private readonly System.Threading.Timer _timer;
        private int _cancelled;

        public SystemOneShotTimer(TimeSpan dueTime, Action callback)
        {
            _timer = new System.Threading.Timer(
                _ =>
                {
                    if (Volatile.Read(ref _cancelled) == 0)
                    {
                        callback();
                    }
                },
                null,
                dueTime,
                Timeout.InfiniteTimeSpan);
        }

        public void Cancel()
        {
            Interlocked.Exchange(ref _cancelled, 1);
            try
            {
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }
        }

        public void Dispose()
        {
            Cancel();
            _timer.Dispose();
        }
    }
}

/// <summary>
/// Debounces display-change and Explorer-restart triggers (SPEC §16), honours <see cref="Settings.RestoreAutomatically"/>
/// on each fire, serializes overlapping triggers by measuring quiet time from the last trigger, and drains cleanly on shutdown.
/// </summary>
internal sealed class AutoRestore : IDisposable
{
    private readonly LayoutService _service;
    private readonly SettingsStore _settings;
    private readonly ITimerFactory _timers;
    private readonly TimeSpan _defaultSettle;
    private readonly TimeProvider _clock;
    private readonly Func<Func<Task>, Task> _runOffUiThread;
    private readonly Lock _sync = new();

    private IOneShotTimer? _activeTimer;
    private long _generation;
    private string? _armedReason;
    private bool _running;
    private Task _activeTask = Task.CompletedTask;
    private bool _retriggerPending;
    private string? _retriggerReason;
    private long _retriggerTimestamp;
    private TimeSpan _retriggerSettle;
    private bool _stopping;
    private bool _disposed;
    private Action? _whenDrained;

    public AutoRestore(
        LayoutService service,
        SettingsStore settings,
        Log log,
        ITimerFactory timers,
        TimeSpan settle,
        TimeProvider? clock = null,
        Func<Func<Task>, Task>? runOffUiThread = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ = log ?? throw new ArgumentNullException(nameof(log));
        _timers = timers ?? throw new ArgumentNullException(nameof(timers));
        _defaultSettle = settle < TimeSpan.Zero ? TimeSpan.Zero : settle;
        _clock = clock ?? TimeProvider.System;
        _runOffUiThread = runOffUiThread ?? (work => Task.Run(work));
    }

    /// <summary>
    /// Raised after an automatic restore runs (never when <see cref="Settings.RestoreAutomatically"/> is off).
    /// <c>silent</c> is <see langword="true"/> for <see cref="Refusal.NoLayout"/> (SPEC §16).
    /// </summary>
    public event Action<OperationResult, string, bool>? Finished;

    /// <summary>Test seam raised when a one-shot settle timer is armed, carrying its reason and due interval.</summary>
    internal event Action<string, TimeSpan>? TimerArmed;

    /// <summary>Schedules (or reschedules) an automatic restore after <paramref name="settleOverride"/> or the default settle interval.</summary>
    public void Trigger(string reason, TimeSpan? settleOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        TimeSpan due = settleOverride ?? _defaultSettle;
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        IOneShotTimer? timerToDispose = null;
        bool armed = false;
        lock (_sync)
        {
            if (_disposed || _stopping)
            {
                return;
            }

            if (_running)
            {
                _retriggerPending = true;
                _retriggerReason = reason;
                _retriggerTimestamp = _clock.GetTimestamp();
                _retriggerSettle = due;
                return;
            }

            timerToDispose = ArmTimerLocked(reason, due);
            armed = true;
        }

        timerToDispose?.Dispose();
        if (armed)
        {
            TimerArmed?.Invoke(reason, due);
        }
    }

    /// <summary>
    /// Stops accepting new triggers, cancels any pending timer, and invokes <paramref name="onDrained"/> once any
    /// in-flight automatic restore (including its <see cref="Finished"/> delivery) has completed.
    /// </summary>
    public void BeginDrain(Action onDrained)
    {
        ArgumentNullException.ThrowIfNull(onDrained);
        IOneShotTimer? timerToDispose;
        bool invokeNow = false;

        lock (_sync)
        {
            _stopping = true;
            _generation++;
            _retriggerPending = false;
            _retriggerReason = null;
            timerToDispose = DetachActiveTimerLocked();
            if (!_running)
            {
                invokeNow = true;
            }
            else
            {
                _whenDrained += onDrained;
            }
        }

        timerToDispose?.Dispose();
        if (invokeNow)
        {
            onDrained();
        }
    }

    /// <summary>
    /// Stops accepting triggers, cancels pending timers, and waits until any already-admitted in-flight restore
    /// finishes so callers never dispose <see cref="Composition"/> while the restore still uses its desktop worker.
    /// </summary>
    public void Dispose()
    {
        Task activeTask;
        IOneShotTimer? timerToDispose;
        lock (_sync)
        {
            _stopping = true;
            _disposed = true;
            _generation++;
            _retriggerPending = false;
            _retriggerReason = null;
            _whenDrained = null;
            timerToDispose = DetachActiveTimerLocked();
            activeTask = _activeTask;
        }

        timerToDispose?.Dispose();
        if (!activeTask.IsCompleted)
        {
            try
            {
                activeTask.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // In-flight restore errors are handled inside RunAdmittedAsync.
            }
        }
    }

    private IOneShotTimer? ArmTimerLocked(string reason, TimeSpan due)
    {
        IOneShotTimer? previous = DetachActiveTimerLocked();
        long gen = ++_generation;
        _armedReason = reason;
        _activeTimer = _timers.Create(due, () => OnTimerFired(gen));
        return previous;
    }

    private IOneShotTimer? DetachActiveTimerLocked()
    {
        IOneShotTimer? previous = _activeTimer;
        _activeTimer = null;
        _armedReason = null;
        previous?.Cancel();
        return previous;
    }

    private void OnTimerFired(long firedGeneration)
    {
        string? reason;
        IOneShotTimer? timerToDispose;
        TaskCompletionSource tcs;

        lock (_sync)
        {
            if (_disposed || _stopping || _running || firedGeneration != _generation)
            {
                return;
            }

            reason = _armedReason;
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            timerToDispose = _activeTimer;
            _activeTimer = null;
            _armedReason = null;
            _running = true;
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeTask = tcs.Task;
        }

        timerToDispose?.Dispose();
        _ = RunAdmittedAsync(reason, tcs);
    }

    private async Task RunAdmittedAsync(string reason, TaskCompletionSource completion)
    {
        try
        {
            await _runOffUiThread(() =>
            {
                if (_settings.Load().Value.RestoreAutomatically)
                {
                    OperationResult result = _service.Restore("automatic, " + reason);
                    bool silent = result.Kind == OperationKind.Refused && result.Refusal == Refusal.NoLayout;
                    bool shouldNotify;
                    lock (_sync)
                    {
                        shouldNotify = !_disposed;
                    }

                    if (shouldNotify)
                    {
                        Finished?.Invoke(result, reason, silent);
                    }
                }

                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        finally
        {
            Action? drainCallbacks = null;
            IOneShotTimer? timerToDispose = null;
            string? armedNextReason = null;
            TimeSpan armedNextDue = TimeSpan.Zero;

            lock (_sync)
            {
                _running = false;
                if (_stopping || _disposed)
                {
                    _retriggerPending = false;
                    _retriggerReason = null;
                    drainCallbacks = _whenDrained;
                    _whenDrained = null;
                }
                else if (_retriggerPending && !string.IsNullOrWhiteSpace(_retriggerReason))
                {
                    string nextReason = _retriggerReason;
                    TimeSpan elapsed = _clock.GetElapsedTime(_retriggerTimestamp);
                    TimeSpan remaining = _retriggerSettle - elapsed;
                    if (remaining < TimeSpan.Zero)
                    {
                        remaining = TimeSpan.Zero;
                    }

                    _retriggerPending = false;
                    _retriggerReason = null;
                    timerToDispose = ArmTimerLocked(nextReason, remaining);
                    armedNextReason = nextReason;
                    armedNextDue = remaining;
                }
            }

            timerToDispose?.Dispose();
            if (armedNextReason is not null)
            {
                TimerArmed?.Invoke(armedNextReason, armedNextDue);
            }

            completion.TrySetResult();
            drainCallbacks?.Invoke();
        }
    }
}
