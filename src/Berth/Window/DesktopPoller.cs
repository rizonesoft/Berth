using System.Windows.Threading;

namespace Berth.Window;

/// <summary>Timer seam for <see cref="DesktopPoller"/> so unit tests control ticks deterministically without sleeps.</summary>
internal interface IPollerTimer : IDisposable
{
    event EventHandler? Tick;

    void Start(TimeSpan interval);

    void Stop();
}

internal sealed class DispatcherPollerTimer(Dispatcher dispatcher) : IPollerTimer
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal, dispatcher);
    private bool _disposed;

    public event EventHandler? Tick
    {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    public void Start(TimeSpan interval)
    {
        if (_disposed)
        {
            return;
        }

        _timer.Interval = interval;
        _timer.Start();
    }

    public void Stop()
    {
        if (!_disposed)
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }
}

/// <summary>
/// Polls desktop state while the main window is visible (SPEC §4): reads immediately on <see cref="Start"/>, on each
/// timer tick, and on <see cref="TriggerNow"/> (window activation), skipping overlapping reads and ignoring any tick
/// or trigger after <see cref="Stop"/>.
/// </summary>
internal sealed class DesktopPoller : IDisposable
{
    private readonly Func<Task> _refreshAsync;
    private readonly IPollerTimer _timer;
    private readonly TimeSpan _interval;
    private readonly Lock _sync = new();

    private bool _started;
    private bool _disposed;
    private int _inFlight;
    private Task _currentRefreshTask = Task.CompletedTask;

    public DesktopPoller(Func<Task> refreshAsync, IPollerTimer timer, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);
        ArgumentNullException.ThrowIfNull(timer);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Poll interval must be positive.");
        }

        _refreshAsync = refreshAsync;
        _timer = timer;
        _interval = interval;
        _timer.Tick += OnTimerTick;
    }

    internal bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                return _started;
            }
        }
    }

    internal Task CurrentRefreshTask
    {
        get
        {
            lock (_sync)
            {
                return _currentRefreshTask;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _started = true;
        }

        _timer.Start(_interval);
        TryStartRefresh();
    }

    public void TriggerNow()
    {
        TryStartRefresh();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _started = false;
        }

        _timer.Stop();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
        }

        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        _timer.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        TryStartRefresh();
    }

    private void TryStartRefresh()
    {
        lock (_sync)
        {
            if (!_started || _disposed)
            {
                return;
            }
        }

        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return;
        }

        Task task = RunRefreshAsync();
        lock (_sync)
        {
            _currentRefreshTask = task;
        }
    }

    private async Task RunRefreshAsync()
    {
        try
        {
            await _refreshAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Refresh failures are surfaced on MainViewModel; release the overlap guard so the next visible tick retries.
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }
}
