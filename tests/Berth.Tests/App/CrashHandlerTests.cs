using Berth.App;
using Berth.Core;
using Berth.Tests.Fakes;

namespace Berth.Tests.App;

public sealed class CrashHandlerTests
{
    [Fact]
    public void HandleLogsIndentedStackAndInnerExceptionRedactsSourceAndShowsExactTwoLineRedactedMessage()
    {
        using var data = new TestData();
        string fakeProfile = Path.Combine(data.Root, "AliceFolder");
        string scopedDataRoot = Path.Combine(fakeProfile, "BerthData");
        BerthPaths paths = BerthPaths.ForRoot(scopedDataRoot);
        var log = new Log(paths, TimeProvider.System);

        string? shown = null;
        var handler = new CrashHandler(
            log,
            msg =>
            {
                shown = msg;
                return true;
            },
            userProfile: fakeProfile,
            userName: "AliceUser",
            exitProcess: _ => { });

        Exception thrown = CaptureThrownException(
            $"Failed reading {fakeProfile}\\Desktop for AliceUser...\r\nSecond line...",
            new IOException($"Inner denied at {fakeProfile.Replace('\\', '/')}/file.txt by aliceuser"));

        handler.Handle(thrown, $@"{fakeProfile}\worker-AliceUser");

        Assert.NotNull(shown);
        string[] dialogLines = shown.Split('\n');
        Assert.Equal(2, dialogLines.Length);
        Assert.Equal(
            "Something went wrong: Failed reading <profile>\\Desktop for <user>... Second line. The details are in the log.",
            dialogLines[0]);
        Assert.Equal(@"<profile>\BerthData\local\logs\berth.log", dialogLines[1]);
        Assert.DoesNotContain("AliceFolder", shown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AliceUser", shown, StringComparison.OrdinalIgnoreCase);

        string[] logLines = File.ReadAllLines(paths.LogFile);
        Assert.True(logLines.Length > 3, "Expected header line plus indented exception and stack lines.");
        Assert.Contains(@" FAIL <profile>\worker-<user>: Something went wrong: ", logLines[0], StringComparison.Ordinal);
        Assert.Contains(logLines, l => l.StartsWith("  System.InvalidOperationException:", StringComparison.Ordinal));
        Assert.Contains(logLines, l => l.Contains("System.IO.IOException: Inner denied at <profile>/file.txt by <user>", StringComparison.Ordinal));
        Assert.Contains(logLines, l => l.StartsWith("     at ", StringComparison.Ordinal) && l.Contains(nameof(CaptureThrownException), StringComparison.Ordinal));

        string fullLog = File.ReadAllText(paths.LogFile);
        Assert.DoesNotContain("AliceFolder", fullLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AliceUser", fullLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactIsSinglePassHandlesMixedSeparatorsShortUsernamesAndEmptyKeys()
    {
        // Username "pro" must NEVER corrupt the "<profile>" marker emitted for the profile path.
        string result = CrashHandler.Redact(
            @"Home C:\Users\pro_folder and mixed C:\Users/pro_folder by PRO",
            userProfile: @"C:\Users\pro_folder",
            userName: "pro");
        Assert.Equal("Home <profile> and mixed <profile> by <user>", result);

        // Blank reason falls back cleanly via Messages.Unexpected("unknown error").
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);
        string? shown = null;
        var handler = new CrashHandler(
            log,
            msg =>
            {
                shown = msg;
                return true;
            },
            userProfile: string.Empty,
            userName: string.Empty,
            exitProcess: _ => { });

        handler.Handle(new InvalidOperationException("   "), "TestSource");
        Assert.NotNull(shown);
        string[] lines = shown.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Something went wrong: unknown error. The details are in the log.", lines[0]);
        Assert.Equal(paths.LogFile, lines[1]);
    }

    [Fact]
    public void AttachDrivesAllThreeSourcesLogsEachHeaderAndDetachesCleanlyOnDoubleDispose()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);

        var events = new List<string>();
        var fakeSources = new FakeCrashSources();
        var handler = new CrashHandler(
            log,
            msg =>
            {
                events.Add("dialog:" + msg.Split('\n')[0]);
                return true;
            },
            userProfile: @"C:\Users\TestProfile",
            userName: "TestUser",
            exitProcess: code => events.Add($"exit:{code}"));

        IDisposable attachment = handler.Attach(fakeSources, shutdown: code => events.Add($"shutdown:{code}"));

        static string[] FailHeaders(string logFile) =>
            File.ReadAllLines(logFile).Where(l => !l.StartsWith("  ", StringComparison.Ordinal)).ToArray();

        // 1. DispatcherUnhandledException -> handled, logged, dialog shown, shutdown(1).
        bool dispatcherHandled = false;
        fakeSources.RaiseDispatcher(new InvalidOperationException("UI boom"), () =>
        {
            dispatcherHandled = true;
            events.Add("handled");
        });
        Assert.True(dispatcherHandled);
        Assert.Equal(
            ["handled", "dialog:Something went wrong: UI boom. The details are in the log.", "shutdown:1"],
            events);
        string[] headers1 = FailHeaders(paths.LogFile);
        Assert.Single(headers1);
        Assert.Contains(" FAIL DispatcherUnhandledException: Something went wrong: UI boom.", headers1[0], StringComparison.Ordinal);

        // 2. AppDomain.UnhandledException with Exception -> logged, dialog shown, exitProcess(1).
        events.Clear();
        fakeSources.RaiseAppDomain(new InvalidOperationException("Domain boom"));
        Assert.Equal(
            ["dialog:Something went wrong: Domain boom. The details are in the log.", "exit:1"],
            events);
        string[] headers2 = FailHeaders(paths.LogFile);
        Assert.Equal(2, headers2.Length);
        Assert.Contains(" FAIL AppDomain.UnhandledException: Something went wrong: Domain boom.", headers2[1], StringComparison.Ordinal);

        // 3. AppDomain.UnhandledException with non-Exception payload -> logged, dialog shown, exitProcess(1).
        events.Clear();
        fakeSources.RaiseAppDomain("Raw string failure");
        Assert.Equal(
            ["dialog:Something went wrong: Raw string failure. The details are in the log.", "exit:1"],
            events);
        string[] headers3 = FailHeaders(paths.LogFile);
        Assert.Equal(3, headers3.Length);
        Assert.Contains(" FAIL AppDomain.UnhandledException: Something went wrong: Raw string failure.", headers3[2], StringComparison.Ordinal);

        // 4. TaskScheduler.UnobservedTaskException -> logged, observed, NO dialog, NO shutdown/exit.
        events.Clear();
        bool taskObserved = false;
        fakeSources.RaiseUnobservedTask(new AggregateException(new InvalidOperationException("Bg boom")), () =>
        {
            taskObserved = true;
            events.Add("observed");
        });
        Assert.True(taskObserved);
        Assert.Equal(["observed"], events);
        string[] headers4 = FailHeaders(paths.LogFile);
        Assert.Equal(4, headers4.Length);
        Assert.Contains(" FAIL TaskScheduler.UnobservedTaskException: ", headers4[3], StringComparison.Ordinal);

        // 5. Double-dispose detaches all three handlers cleanly -> zero further deliveries and log stays unchanged.
        string logBeforeDispose = File.ReadAllText(paths.LogFile);
        attachment.Dispose();
        attachment.Dispose();
        Assert.True(fakeSources.Disposed);

        events.Clear();
        bool afterHandled = false;
        bool afterObserved = false;
        fakeSources.RaiseDispatcher(new InvalidOperationException("After"), () => afterHandled = true);
        fakeSources.RaiseAppDomain(new InvalidOperationException("After"));
        fakeSources.RaiseUnobservedTask(new AggregateException(new InvalidOperationException("After")), () => afterObserved = true);
        Assert.False(afterHandled);
        Assert.False(afterObserved);
        Assert.Empty(events);
        Assert.Equal(logBeforeDispose, File.ReadAllText(paths.LogFile));
    }

    [Fact]
    public void ReEntrantDialogThrowingDialogPreDialogFailureAndThrowingPayloadToStringStillCompleteActionsAndReleaseGuard()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);

        int dialogCalls = 0;
        int exitCalls = 0;
        int shutdownCalls = 0;
        CrashHandler? handler = null;
        bool throwInDialog = false;
        bool throwBeforeDialog = false;
        bool reEnterInDialog = true;

        handler = new CrashHandler(
            log,
            _ =>
            {
                dialogCalls++;
                if (reEnterInDialog)
                {
                    reEnterInDialog = false;
                    // Re-enter Handle while dialog is active: must NOT open a second dialog.
                    handler!.Handle(new InvalidOperationException("Nested"), "Nested");
                }

                if (throwInDialog)
                {
                    throw new InvalidOperationException("Dialog failed");
                }

                return true;
            },
            userProfile: @"C:\Users\TestProfile",
            userName: "TestUser",
            exitProcess: code =>
            {
                Assert.Equal(1, code);
                exitCalls++;
            },
            beforeDialog: () =>
            {
                if (throwBeforeDialog)
                {
                    throw new InvalidOperationException("Simulated reporting failure before dialog");
                }
            });

        var sources = new FakeCrashSources();
        using IDisposable attachment = handler.Attach(sources, shutdown: code =>
        {
            Assert.Equal(1, code);
            shutdownCalls++;
        });

        // 1. Re-entrant dialog shows at most once and still completes shutdown(1).
        sources.RaiseDispatcher(new InvalidOperationException("Outer"), () => { });
        Assert.Equal(1, dialogCalls);
        Assert.Equal(1, shutdownCalls);

        // 2. Throwing dialog on AppDomain with payload whose ToString() throws still logs fallback and calls exitProcess(1).
        throwInDialog = true;
        sources.RaiseAppDomain(new ThrowingToStringPayload());
        Assert.Equal(2, dialogCalls);
        Assert.Equal(1, exitCalls);

        // 3. Reporting failure BEFORE dialog on all three sources still runs shutdown(1), exitProcess(1), and setObserved().
        throwInDialog = false;
        throwBeforeDialog = true;
        bool observedOnFailure = false;
        sources.RaiseDispatcher(new InvalidOperationException("PreDialog1"), () => { });
        sources.RaiseAppDomain(new InvalidOperationException("PreDialog2"));
        sources.RaiseUnobservedTask(new AggregateException(new InvalidOperationException("PreDialog3")), () => observedOnFailure = true);
        Assert.Equal(2, shutdownCalls);
        Assert.Equal(2, exitCalls);
        Assert.True(observedOnFailure);

        // 4. Guard was released after failures: a subsequent fatal event still displays a dialog.
        throwBeforeDialog = false;
        sources.RaiseDispatcher(new InvalidOperationException("Later"), () => { });
        Assert.Equal(3, dialogCalls);
        Assert.Equal(3, shutdownCalls);

        string logText = File.ReadAllText(paths.LogFile);
        Assert.Contains(nameof(ThrowingToStringPayload), logText, StringComparison.Ordinal);
    }

    private static Exception CaptureThrownException(string message, Exception inner)
    {
        try
        {
            throw new InvalidOperationException(message, inner);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class ThrowingToStringPayload
    {
        public override string ToString() => throw new InvalidOperationException("ToString exploded");
    }

    private sealed class FakeCrashSources : ICrashSources
    {
        public bool Disposed { get; private set; }

        public event Action<Exception, Action>? DispatcherUnhandled;

        public event Action<object?>? AppDomainUnhandled;

        public event Action<AggregateException, Action>? UnobservedTask;

        public void RaiseDispatcher(Exception ex, Action markHandled) => DispatcherUnhandled?.Invoke(ex, markHandled);

        public void RaiseAppDomain(object? payload) => AppDomainUnhandled?.Invoke(payload);

        public void RaiseUnobservedTask(AggregateException ex, Action setObserved) => UnobservedTask?.Invoke(ex, setObserved);

        public void Dispose() => Disposed = true;
    }
}
