using System.Text;
using System.Windows;
using System.Windows.Threading;
using Berth.Core;
using WpfApplication = System.Windows.Application;

namespace Berth.App;

/// <summary>Abstraction over the three runtime unhandled-exception sources so <see cref="CrashHandler.Attach"/> can be tested directly.</summary>
internal interface ICrashSources : IDisposable
{
    event Action<Exception, Action>? DispatcherUnhandled;

    event Action<object?>? AppDomainUnhandled;

    event Action<AggregateException, Action>? UnobservedTask;
}

/// <summary>
/// Logs, redacts, displays, and terminates cleanly on unhandled exceptions in the tray app (SPEC §18, last bullet).
/// </summary>
internal sealed class CrashHandler
{
    private readonly Log _log;
    private readonly Func<string, bool> _showMessage;
    private readonly string? _userProfile;
    private readonly string? _userName;
    private readonly Action<int> _exitProcess;
    private readonly Action? _beforeDialog;
    private int _dialogActive;

    public CrashHandler(
        Log log,
        Func<string, bool> showMessage,
        string? userProfile = null,
        string? userName = null,
        Action<int>? exitProcess = null)
        : this(log, showMessage, userProfile, userName, exitProcess, beforeDialog: null)
    {
    }

    internal CrashHandler(
        Log log,
        Func<string, bool> showMessage,
        string? userProfile,
        string? userName,
        Action<int>? exitProcess,
        Action? beforeDialog)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(showMessage);
        _log = log;
        _showMessage = showMessage;
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _userName = userName ?? Environment.UserName;
        _exitProcess = exitProcess ?? Environment.Exit;
        _beforeDialog = beforeDialog;
    }

    /// <summary>
    /// Logs a redacted <c>FAIL</c> record (header with <paramref name="source"/> plus indented redacted exception
    /// details) and, when <paramref name="showDialog"/> is true and no dialog is already open, shows the two-line
    /// SPEC §18 message with the redacted log path. Never throws.
    /// </summary>
    public void Handle(Exception error, string source, bool showDialog = true)
    {
        ArgumentNullException.ThrowIfNull(error);
        string safeSource = string.IsNullOrWhiteSpace(source) ? "UnhandledException" : source;
        string rawReason = SafeExceptionMessage(error);
        string rawDetails = SafeExceptionToString(error);
        HandleCore(rawReason, rawDetails, safeSource, showDialog);
    }

    /// <summary>Handles an <see cref="AppDomain.UnhandledException"/> payload that may or may not be an <see cref="Exception"/>.</summary>
    internal void HandleObject(object? exceptionObject, string source, bool showDialog = true)
    {
        if (exceptionObject is Exception ex)
        {
            Handle(ex, source, showDialog);
            return;
        }

        string safeSource = string.IsNullOrWhiteSpace(source) ? "AppDomain.UnhandledException" : source;
        string rawReason = SafeObjectText(exceptionObject);
        HandleCore(rawReason, rawReason, safeSource, showDialog);
    }

    /// <summary>Attaches to WPF's dispatcher, the current <see cref="AppDomain"/>, and <see cref="TaskScheduler.UnobservedTaskException"/>.</summary>
    public IDisposable Attach(WpfApplication? app, Action<int>? shutdown = null)
    {
        Action<int> effectiveShutdown = shutdown ?? (code =>
        {
            if (app is not null)
            {
                app.Shutdown(code);
            }
            else
            {
                _exitProcess(code);
            }
        });
        return Attach(new RuntimeCrashSources(app), effectiveShutdown);
    }

    /// <summary>Attaches to <paramref name="sources"/>; disposing the returned handle detaches all three subscriptions idempotently.</summary>
    internal IDisposable Attach(ICrashSources sources, Action<int>? shutdown = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Action<int> effectiveShutdown = shutdown ?? _exitProcess;

        void OnDispatcher(Exception ex, Action markHandled)
        {
            markHandled();
            try
            {
                Handle(ex, "DispatcherUnhandledException", showDialog: true);
            }
            catch (Exception)
            {
                // Completion action below must still shut down with exit code 1 even if reporting fails.
            }
            finally
            {
                effectiveShutdown(1);
            }
        }

        void OnAppDomain(object? payload)
        {
            try
            {
                HandleObject(payload, "AppDomain.UnhandledException", showDialog: true);
            }
            catch (Exception)
            {
                // Completion action below must still exit with code 1 even if reporting fails.
            }
            finally
            {
                _exitProcess(1);
            }
        }

        void OnUnobservedTask(AggregateException ex, Action setObserved)
        {
            try
            {
                Handle(ex, "TaskScheduler.UnobservedTaskException", showDialog: false);
            }
            catch (Exception)
            {
                // Completion action below must still observe the task even if reporting fails.
            }
            finally
            {
                setObserved();
            }
        }

        sources.DispatcherUnhandled += OnDispatcher;
        sources.AppDomainUnhandled += OnAppDomain;
        sources.UnobservedTask += OnUnobservedTask;

        return new Attachment(sources, OnDispatcher, OnAppDomain, OnUnobservedTask);
    }

    /// <summary>
    /// Single-pass case-insensitive redaction replacing <paramref name="userProfile"/> (with either <c>\</c> or
    /// <c>/</c> separators) with <c>&lt;profile&gt;</c> and <paramref name="userName"/> with <c>&lt;user&gt;</c>.
    /// Emitted markers are never re-scanned, so a username such as <c>pro</c> cannot corrupt <c>&lt;profile&gt;</c>.
    /// </summary>
    internal static string Redact(string text, string? userProfile, string? userName)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string? profile = string.IsNullOrWhiteSpace(userProfile) ? null : userProfile.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(profile))
        {
            profile = null;
        }

        string? user = string.IsNullOrWhiteSpace(userName) ? null : userName;
        if (profile is null && user is null)
        {
            return text;
        }

        var output = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (profile is not null && MatchesPathAt(text, i, profile))
            {
                output.Append("<profile>");
                i += profile.Length;
                continue;
            }

            if (user is not null && MatchesOrdinalIgnoreCaseAt(text, i, user))
            {
                output.Append("<user>");
                i += user.Length;
                continue;
            }

            output.Append(text[i]);
            i++;
        }

        return output.ToString();
    }

    private void HandleCore(string rawReason, string rawDetails, string source, bool showDialog)
    {
        string redactedSource = Redact(source, _userProfile, _userName);
        string redactedReason = Redact(rawReason, _userProfile, _userName);
        string redactedDetails = Redact(rawDetails, _userProfile, _userName);
        string sentence = Messages.Unexpected(redactedReason);

        _log.FailWithDetails($"{redactedSource}: {sentence}", redactedDetails);
        _beforeDialog?.Invoke();

        if (!showDialog)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _dialogActive, 1, 0) != 0)
        {
            return;
        }

        try
        {
            string redactedLogPath = Redact(_log.FilePath, _userProfile, _userName);
            string dialogText = $"{sentence}\n{redactedLogPath}";
            _ = _showMessage(dialogText);
        }
        catch (Exception)
        {
            // A failing crash dialog must never abort shutdown or process exit.
        }
        finally
        {
            Volatile.Write(ref _dialogActive, 0);
        }
    }

    private static string SafeExceptionMessage(Exception error)
    {
        try
        {
            return error.Message ?? string.Empty;
        }
        catch (Exception)
        {
            return error.GetType().FullName ?? "Unhandled error";
        }
    }

    private static string SafeExceptionToString(Exception error)
    {
        try
        {
            return error.ToString();
        }
        catch (Exception)
        {
            return error.GetType().FullName ?? "Unhandled error";
        }
    }

    private static string SafeObjectText(object? value)
    {
        if (value is null)
        {
            return "Unhandled error";
        }

        try
        {
            return value.ToString() ?? value.GetType().FullName ?? "Unhandled error";
        }
        catch (Exception)
        {
            return value.GetType().FullName ?? "Unhandled error";
        }
    }

    private static bool MatchesPathAt(string text, int index, string path)
    {
        if (index + path.Length > text.Length)
        {
            return false;
        }

        for (int j = 0; j < path.Length; j++)
        {
            char tc = text[index + j];
            char pc = path[j];
            if ((tc is '\\' or '/') && (pc is '\\' or '/'))
            {
                continue;
            }

            if (char.ToUpperInvariant(tc) != char.ToUpperInvariant(pc))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesOrdinalIgnoreCaseAt(string text, int index, string needle) =>
        index + needle.Length <= text.Length
        && text.AsSpan(index, needle.Length).Equals(needle.AsSpan(), StringComparison.OrdinalIgnoreCase);

    private sealed class Attachment(
        ICrashSources sources,
        Action<Exception, Action> onDispatcher,
        Action<object?> onAppDomain,
        Action<AggregateException, Action> onUnobservedTask) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            sources.DispatcherUnhandled -= onDispatcher;
            sources.AppDomainUnhandled -= onAppDomain;
            sources.UnobservedTask -= onUnobservedTask;
            sources.Dispose();
        }
    }

    private sealed class RuntimeCrashSources : ICrashSources
    {
        private readonly WpfApplication? _app;
        private int _disposed;

        public RuntimeCrashSources(WpfApplication? app)
        {
            _app = app;
            if (_app is not null)
            {
                _app.DispatcherUnhandledException += HandleDispatcher;
            }

            AppDomain.CurrentDomain.UnhandledException += HandleAppDomain;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTask;
        }

        public event Action<Exception, Action>? DispatcherUnhandled;

        public event Action<object?>? AppDomainUnhandled;

        public event Action<AggregateException, Action>? UnobservedTask;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_app is not null)
            {
                _app.DispatcherUnhandledException -= HandleDispatcher;
            }

            AppDomain.CurrentDomain.UnhandledException -= HandleAppDomain;
            TaskScheduler.UnobservedTaskException -= HandleUnobservedTask;
        }

        private void HandleDispatcher(object sender, DispatcherUnhandledExceptionEventArgs e) =>
            DispatcherUnhandled?.Invoke(e.Exception, () => e.Handled = true);

        private void HandleAppDomain(object sender, UnhandledExceptionEventArgs e) =>
            AppDomainUnhandled?.Invoke(e.ExceptionObject);

        private void HandleUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e) =>
            UnobservedTask?.Invoke(e.Exception, e.SetObserved);
    }
}
