using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace Berth.App;

/// <summary>
/// The WPF <see cref="WpfApplication"/> that hosts Berth's tray icon and window, following the Windows theme live
/// with <see cref="ShutdownMode.OnExplicitShutdown"/> (SPEC §2, §3).
/// </summary>
internal sealed class BerthApplication : WpfApplication
{
    private readonly Func<bool> _readHighContrast;
    private readonly Func<bool> _readAppsUseLightTheme;
    private bool _themeInitialized;
    private bool _subscribed;

    public BerthApplication()
        : this(readHighContrast: null, readAppsUseLightTheme: null)
    {
    }

    internal BerthApplication(Func<bool>? readHighContrast, Func<bool>? readAppsUseLightTheme)
    {
        _readHighContrast = readHighContrast ?? (() => SystemParameters.HighContrast);
        _readAppsUseLightTheme = readAppsUseLightTheme ?? (() => ThemeResources.ReadAppsUseLightTheme());

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    /// <summary>The colour theme currently merged into <see cref="WpfApplication.Resources"/>.</summary>
    public ColorTheme CurrentColorTheme { get; private set; }

    /// <summary>Subscribes to Windows theme and High Contrast changes, enables <see cref="ThemeMode.System"/>, and merges the initial token dictionaries.</summary>
    public void InitializeTheme()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        }

        if (!_themeInitialized)
        {
            _themeInitialized = true;
#pragma warning disable WPF0001 // WPF ThemeMode is experimental in .NET 11; SPEC §3 requires ThemeMode.System.
            ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
            ApplySystemTheme();
        }
    }

    /// <summary>Re-evaluates High Contrast and Windows app theme and applies the matching colour dictionary.</summary>
    public void ApplySystemTheme()
    {
        ColorTheme selected = ThemeResources.Select(_readHighContrast(), _readAppsUseLightTheme());
        ApplyColorTheme(selected);
    }

    /// <summary>Merges <paramref name="theme"/> directly into <see cref="WpfApplication.Resources"/>.</summary>
    public void ApplyColorTheme(ColorTheme theme)
    {
        ThemeResources.Apply(Resources, theme);
        CurrentColorTheme = theme;
    }

    /// <summary>Test seam driving the Windows <see cref="SystemEvents.UserPreferenceChanged"/> handler.</summary>
    internal void OnUserPreferenceChanged(UserPreferenceCategory category)
    {
        if (category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
        {
            MarshalApplySystemTheme();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        InitializeTheme();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        }

        base.OnExit(e);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            MarshalApplySystemTheme();
        }
    }

    private void OnSystemUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        OnUserPreferenceChanged(e.Category);

    private void MarshalApplySystemTheme()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplySystemTheme();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(ApplySystemTheme);
        }
    }
}
