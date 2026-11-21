using Berth.Core;
using Berth.Desktop;

namespace Berth.App;

/// <summary>
/// Production composition root wiring Core and Desktop services for one command or app session, owning disposal of
/// the underlying <see cref="ShellDesktopIcons"/> STA worker even when wrapped in <see cref="ScopedDesktopIcons"/>.
/// </summary>
internal sealed class Composition : IDisposable
{
    private readonly IDisposable? _ownedDesktop;
    private bool _disposed;

    private Composition(
        BerthPaths paths,
        Log log,
        IMonitors monitors,
        IDesktopIcons desktop,
        IDisposable? ownedDesktop,
        LayoutStore store,
        OperationLock gate,
        LayoutService service)
    {
        Paths = paths;
        Log = log;
        Monitors = monitors;
        Desktop = desktop;
        _ownedDesktop = ownedDesktop;
        Store = store;
        Gate = gate;
        Service = service;
    }

    public BerthPaths Paths { get; }
    public Log Log { get; }
    public IMonitors Monitors { get; }
    public IDesktopIcons Desktop { get; }
    public LayoutStore Store { get; }
    public OperationLock Gate { get; }
    public LayoutService Service { get; }

    public static Composition Create(
        BerthPaths paths,
        Log log,
        Func<string, string?>? getenv = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(log);

        TimeProvider effectiveClock = clock ?? TimeProvider.System;
        var monitors = new SystemMonitors();
        var shellDesktop = new ShellDesktopIcons(monitors, log);
        try
        {
            IDesktopIcons desktop = ApplyTestIconScope(paths, shellDesktop, getenv);
            var store = new LayoutStore(paths);
            var gate = OperationLock.ForApp(paths);
            var service = new LayoutService(desktop, monitors, store, paths, gate, log, effectiveClock);
            return new Composition(paths, log, monitors, desktop, shellDesktop, store, gate, service);
        }
        catch
        {
            shellDesktop.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wraps <paramref name="desktop"/> in <see cref="ScopedDesktopIcons"/> strictly when
    /// <see cref="BerthPaths.TestMode"/> is <see langword="true"/> and <c>BERTH_TEST_ICON_PREFIX</c> is non-blank
    /// (SPEC §14).
    /// </summary>
    internal static IDesktopIcons ApplyTestIconScope(
        BerthPaths paths,
        IDesktopIcons desktop,
        Func<string, string?>? getenv = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(desktop);

        if (!paths.TestMode)
        {
            return desktop;
        }

        string? prefix = (getenv ?? Environment.GetEnvironmentVariable)("BERTH_TEST_ICON_PREFIX");
        return string.IsNullOrWhiteSpace(prefix)
            ? desktop
            : new ScopedDesktopIcons(desktop, prefix);
    }

    internal static Composition ForTest(
        BerthPaths paths,
        Log log,
        IMonitors monitors,
        IDesktopIcons desktop,
        LayoutStore store,
        OperationLock gate,
        LayoutService service,
        IDisposable? ownedDesktop = null) =>
        new(paths, log, monitors, desktop, ownedDesktop: ownedDesktop, store, gate, service);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownedDesktop?.Dispose();
    }
}
