using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Xunit.Sdk;
using Xunit.v3;

namespace Berth.Tests;

/// <summary>
/// Helpers and xUnit v3 fact attribute for tests that must run on an <see cref="ApartmentState.STA"/> thread with a
/// pumping WPF <see cref="Dispatcher"/>.
/// </summary>
public static class Sta
{
    public static bool HasInteractiveDesktopSession =>
        Environment.UserInteractive && Process.GetCurrentProcess().SessionId != 0;

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Run(() =>
        {
            action();
            return true;
        });
    }

    public static T Run<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return RunAsync(() => Task.FromResult(func()));
    }

    public static void RunAsync(Func<Task> asyncWork)
    {
        ArgumentNullException.ThrowIfNull(asyncWork);
        RunAsync(async () =>
        {
            await asyncWork();
            return true;
        });
    }

    public static T RunAsync<T>(Func<Task<T>> asyncWork)
    {
        ArgumentNullException.ThrowIfNull(asyncWork);
        return RunAsync(asyncWork, out _);
    }

    internal static T RunAsync<T>(Func<Task<T>> asyncWork, out Dispatcher? usedDispatcher)
    {
        ArgumentNullException.ThrowIfNull(asyncWork);
        Dispatcher? captured = null;
        try
        {
            return StartAsyncCore(asyncWork, dispatcher => captured = dispatcher).GetAwaiter().GetResult();
        }
        finally
        {
            usedDispatcher = captured;
        }
    }

    internal static Task<T> StartAsyncCore<T>(
        Func<Task<T>> asyncWork,
        Action<Dispatcher>? onDispatcherCreated = null)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher? dispatcher = null;
            Task<T>? workTask = null;
            Exception? syncOrPumpFailure = null;
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                onDispatcherCreated?.Invoke(dispatcher);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

                var frame = new DispatcherFrame();
                workTask = asyncWork();
                _ = workTask.ContinueWith(
                    completed =>
                    {
                        _ = dispatcher.InvokeAsync(() => { frame.Continue = false; }, DispatcherPriority.Send);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                Dispatcher.PushFrame(frame);
            }
            catch (Exception error)
            {
                syncOrPumpFailure = error;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
                if (dispatcher is not null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.InvokeShutdown();
                }

                if (syncOrPumpFailure is not null)
                {
                    tcs.TrySetException(syncOrPumpFailure);
                }
                else if (workTask is null)
                {
                    tcs.TrySetException(new InvalidOperationException("STA work task was not created."));
                }
                else if (workTask.IsCanceled)
                {
                    tcs.TrySetCanceled();
                }
                else if (workTask.IsFaulted)
                {
                    tcs.TrySetException(workTask.Exception!.InnerExceptions);
                }
                else
                {
                    tcs.TrySetResult(workTask.Result);
                }
            }
        })
        {
            IsBackground = true,
            Name = "StaFact",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

[XunitTestCaseDiscoverer(typeof(StaFactDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class StaFactAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1)
    : FactAttribute(sourceFilePath, sourceLineNumber);

public sealed class StaFactDiscoverer : FactDiscoverer
{
    protected override IXunitTestCase CreateTestCase(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute)
    {
        IXunitTestCase inner = base.CreateTestCase(discoveryOptions, testMethod, factAttribute);
        if (inner is not XunitTestCase xtc || inner is ExecutionErrorTestCase)
        {
            return inner;
        }

        return new StaTestCase(xtc);
    }
}

public sealed class StaTestCase : XunitTestCase, ISelfExecutingXunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public StaTestCase()
    {
    }

    public StaTestCase(XunitTestCase source)
        : base(
            source.TestMethod,
            source.TestCaseDisplayName,
            source.UniqueID,
            source.Explicit,
            source.TestLabel,
            source.DisableParallelization,
            source.SkipExceptions,
            source.SkipReason,
            source.SkipType,
            source.SkipUnless,
            source.SkipWhen,
            new Dictionary<string, HashSet<string>>(source.Traits, StringComparer.OrdinalIgnoreCase),
            source.TestMethodArguments,
            source.SourceFilePath,
            source.SourceLineNumber,
            source.Timeout == 0 ? null : source.Timeout)
    {
    }

    public ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler scheduler,
        FixtureMappingManager methodFixtureMappings)
    {
        Task<RunSummary> task = Sta.StartAsyncCore(async () =>
        {
            IReadOnlyCollection<IXunitTest> tests = await CreateTests();
            return await XunitTestCaseRunner.Instance.Run(
                this,
                tests,
                messageBus,
                aggregator,
                cancellationTokenSource,
                parallelMode,
                scheduler,
                TestCaseDisplayName,
                SkipReason,
                explicitOption,
                constructorArguments,
                methodFixtureMappings);
        });
        return new ValueTask<RunSummary>(task);
    }
}
