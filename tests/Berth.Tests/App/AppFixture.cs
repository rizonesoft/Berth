using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Berth.App;

namespace Berth.Tests.App;

/// <summary>
/// Hosts the single process-wide <see cref="BerthApplication"/> on a dedicated STA thread with a running
/// <see cref="Dispatcher"/> so multiple test classes in the <c>Desktop</c> collection share one WPF application
/// without hitting WPF's one-Application-per-AppDomain limit.
/// </summary>
public sealed class AppFixture : IDisposable
{
    private readonly Thread _staThread;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private ExceptionDispatchInfo? _startupError;
    private BerthApplication? _app;
    private bool? _highContrastOverride;
    private bool? _appsUseLightThemeOverride;
    private bool _disposed;

    public AppFixture()
    {
        _staThread = new Thread(RunApp)
        {
            IsBackground = true,
            Name = "Berth.Tests.AppFixture.STA",
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("AppFixture failed to start BerthApplication within 15 seconds.");
        }

        _startupError?.Throw();
    }

    internal BerthApplication App =>
        _app ?? throw new InvalidOperationException("BerthApplication did not start.");

    public int ExitCode { get; private set; } = -1;

    /// <summary>Executes <paramref name="action"/> synchronously on the STA application dispatcher.</summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);
        App.Dispatcher.Invoke(action);
    }

    /// <summary>Executes <paramref name="func"/> synchronously on the STA application dispatcher.</summary>
    public T Invoke<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return App.Dispatcher.Invoke(func);
    }

    /// <summary>Overrides the system theme predicates read by <see cref="BerthApplication.ApplySystemTheme"/> for a test.</summary>
    internal void SetThemeOverrides(bool? highContrast, bool? appsUseLightTheme)
    {
        _highContrastOverride = highContrast;
        _appsUseLightThemeOverride = appsUseLightTheme;
    }

    /// <summary>Clears any test theme overrides and reapplies the host's real Windows theme.</summary>
    internal void ResetThemeOverrides()
    {
        _highContrastOverride = null;
        _appsUseLightThemeOverride = null;
        Invoke(() => App.ApplySystemTheme());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_app is not null && !_app.Dispatcher.HasShutdownStarted)
        {
            _app.Dispatcher.Invoke(() => _app.Shutdown(0));
        }

        _staThread.Join(TimeSpan.FromSeconds(10));
        _ready.Dispose();
    }

    private void RunApp()
    {
        try
        {
            var app = new BerthApplication(
                readHighContrast: () => _highContrastOverride ?? SystemParameters.HighContrast,
                readAppsUseLightTheme: () => _appsUseLightThemeOverride ?? ThemeResources.ReadAppsUseLightTheme());
            app.Startup += (_, _) =>
            {
                _app = app;
                _ready.Set();
            };

            ExitCode = app.Run();
        }
        catch (Exception ex)
        {
            _startupError = ExceptionDispatchInfo.Capture(ex);
            _ready.Set();
        }
    }
}
