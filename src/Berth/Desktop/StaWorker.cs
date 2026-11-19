using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Berth.Desktop;

/// <summary>
/// Owns a single dedicated background <see cref="ApartmentState.STA"/> thread running a message-pumping WPF
/// <see cref="Dispatcher"/> loop so shell COM calls from any thread execute serially on one STA apartment while
/// incoming Win32 and COM messages continue to be dispatched whenever the worker is idle.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private static readonly TimeSpan DefaultDisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly TimeSpan _disposeTimeout;
    private readonly Action? _onItemEnqueued;
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;
    private readonly int _workerThreadId;
    private bool _disposed;

    public StaWorker()
        : this(DefaultDisposeTimeout, onItemEnqueued: null)
    {
    }

    internal StaWorker(TimeSpan disposeTimeout, Action? onItemEnqueued = null)
    {
        if (disposeTimeout < TimeSpan.Zero && disposeTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposeTimeout),
                disposeTimeout,
                "Dispose timeout must be non-negative or Timeout.InfiniteTimeSpan.");
        }

        _disposeTimeout = disposeTimeout;
        _onItemEnqueued = onItemEnqueued;

        using var ready = new ManualResetEventSlim(false);
        Dispatcher? startedDispatcher = null;
        int startedThreadId = 0;
        ExceptionDispatchInfo? startupError = null;

        _thread = new Thread(() =>
        {
            try
            {
                startedThreadId = Environment.CurrentManagedThreadId;
                startedDispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                startupError = ExceptionDispatchInfo.Capture(ex);
                ready.Set();
                return;
            }

            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Berth.StaWorker",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        startupError?.Throw();
        _dispatcher = startedDispatcher
            ?? throw new InvalidOperationException("STA worker failed to initialize its Dispatcher.");
        _workerThreadId = startedThreadId;
    }

    public void Run(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = Run(() =>
        {
            work();
            return true;
        });
    }

    public T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Environment.CurrentManagedThreadId == _workerThreadId)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            return work();
        }

        var item = new WorkItem<T>(work);
        DispatcherOperation operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            operation = _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(item.Execute));
            _onItemEnqueued?.Invoke();
        }

        DispatcherOperationStatus status = operation.Wait();
        if (status != DispatcherOperationStatus.Completed)
        {
            throw new InvalidOperationException(
                $"The STA worker operation did not complete (status: {status}).");
        }

        return item.GetResult();
    }

    public void Dispose()
    {
        bool initiateShutdown = false;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                initiateShutdown = true;
                _dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
        }

        if (initiateShutdown && Environment.CurrentManagedThreadId != _workerThreadId)
        {
            _ = _thread.Join(_disposeTimeout);
        }
    }

    private sealed class WorkItem<T>(Func<T> work)
    {
        private T? _result;
        private ExceptionDispatchInfo? _error;

        public void Execute()
        {
            try
            {
                _result = work();
            }
            catch (Exception ex)
            {
                _error = ExceptionDispatchInfo.Capture(ex);
            }
        }

        public T GetResult()
        {
            _error?.Throw();
            return _result!;
        }
    }
}
