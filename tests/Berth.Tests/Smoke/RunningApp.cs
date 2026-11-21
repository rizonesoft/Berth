using System.Diagnostics;
using System.Windows.Automation;
using Berth.Tests.Cli;

namespace Berth.Tests.Smoke;

/// <summary>
/// Starts the built <c>berth.exe</c> tray app in an isolated test environment (<c>BERTH_DATA</c>, random
/// <c>BERTH_INSTANCE</c>, and <c>BERTH_TEST_ICON_PREFIX</c>), locates its top-level <c>Berth</c> window through
/// UI Automation, drives controls exclusively via UI Automation patterns, and kills the entire process tree on
/// startup failure, timeout, or disposal.
/// </summary>
internal sealed class RunningApp : IDisposable
{
    public static readonly TimeSpan DefaultWindowWaitTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly BerthProcess _process;
    private bool _disposed;

    private RunningApp(BerthProcess process, string instanceName, AutomationElement window)
    {
        _process = process;
        InstanceName = instanceName;
        Window = window;
    }

    public int ProcessId => _process.Id;
    public string InstanceName { get; }
    public AutomationElement Window { get; private set; }
    public bool HasExited => _process.HasExited;

    public static RunningApp Start(
        TestData data,
        string iconPrefix,
        string? instanceName = null,
        TimeSpan? windowWaitTimeout = null,
        Action<int>? onStarted = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPrefix);

        string effectiveInstance = string.IsNullOrWhiteSpace(instanceName)
            ? "test-" + Guid.NewGuid().ToString("N")
            : instanceName;
        TimeSpan timeout = windowWaitTimeout ?? DefaultWindowWaitTimeout;

        BerthProcess process = BerthProcess.Start(
            data.Root,
            [],
            testIconPrefix: iconPrefix,
            extraEnvironment: new Dictionary<string, string>
            {
                ["BERTH_INSTANCE"] = effectiveInstance,
            });

        try
        {
            onStarted?.Invoke(process.Id);
            AutomationElement window = WaitForWindow(process.Id, timeout, () => process.HasExited);
            return new RunningApp(process, effectiveInstance, window);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public static AutomationElement? FindWindow(int processId)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new PropertyCondition(AutomationElement.NameProperty, "Berth"));
        try
        {
            return AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    public static AutomationElement WaitForWindow(int processId, TimeSpan timeout, Func<bool>? hasProcessExited = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (elapsed.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalMilliseconds:F0} ms waiting for top-level 'Berth' window for PID {processId}.");
            }

            if (hasProcessExited?.Invoke() == true)
            {
                throw new InvalidOperationException(
                    $"berth.exe (PID {processId}) exited before its 'Berth' window appeared.");
            }

            AutomationElement? found = FindWindow(processId);
            if (elapsed.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalMilliseconds:F0} ms waiting for top-level 'Berth' window for PID {processId}.");
            }

            if (found is not null)
            {
                return found;
            }

            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                continue;
            }

            Thread.Sleep(remaining < PollInterval ? remaining : PollInterval);
        }
    }

    public static bool WaitForWindowGone(int processId, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (FindWindow(processId) is null)
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        return FindWindow(processId) is null;
    }

    public AutomationElement RefreshWindow(TimeSpan timeout)
    {
        Window = WaitForWindow(_process.Id, timeout, () => _process.HasExited);
        return Window;
    }

    public AutomationElement? TryFindButton(string name)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name));
        try
        {
            return Window.FindFirst(TreeScope.Descendants, condition);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    public AutomationElement WaitForButton(string name, TimeSpan timeout, bool requireEnabled = true)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            AutomationElement? button = TryFindButton(name);
            if (button is not null)
            {
                try
                {
                    if (!requireEnabled || button.Current.IsEnabled)
                    {
                        return button;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Re-query on next poll.
                }
            }

            Thread.Sleep(PollInterval);
        }

        throw new TimeoutException($"Button '{name}' did not become available within {timeout.TotalSeconds:F1}s.");
    }

    public bool WaitForButtonGone(string name, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            AutomationElement? button = TryFindButton(name);
            if (button is null)
            {
                return true;
            }

            try
            {
                if (button.Current.IsOffscreen)
                {
                    return true;
                }
            }
            catch (ElementNotAvailableException)
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        return TryFindButton(name) is null;
    }

    public void InvokeButton(string name, TimeSpan? timeout = null)
    {
        AutomationElement button = WaitForButton(name, timeout ?? TimeSpan.FromSeconds(10), requireEnabled: true);
        var pattern = (InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern);
        pattern.Invoke();
    }

    public void CloseWindow()
    {
        var pattern = (WindowPattern)Window.GetCurrentPattern(WindowPattern.Pattern);
        pattern.Close();
    }

    public string ReadStatusText()
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, "SentenceText");
        try
        {
            AutomationElement? sentence = Window.FindFirst(TreeScope.Descendants, condition);
            return sentence?.Current.Name ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    public string WaitForStatus(Func<string, bool> predicate, string description, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var elapsed = Stopwatch.StartNew();
        string lastObserved = string.Empty;
        while (elapsed.Elapsed < timeout)
        {
            lastObserved = ReadStatusText();
            if (predicate(lastObserved))
            {
                return lastObserved;
            }

            Thread.Sleep(PollInterval);
        }

        throw new TimeoutException(
            $"Timed out after {timeout.TotalSeconds:F1}s waiting for status ({description}). Last observed: '{lastObserved}'.");
    }

    public static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _process.Dispose();
    }
}
