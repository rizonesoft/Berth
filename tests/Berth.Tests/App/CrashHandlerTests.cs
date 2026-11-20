using Berth.App;
using Berth.Core;
using Berth.Tests.Fakes;

namespace Berth.Tests.App;

public sealed class CrashHandlerTests
{
    [Fact]
    public void HandleLogsIndentedStackAndInnerExceptionAndShowsExactTwoLineRedactedMessage()
    {
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
            userProfile: @"C:\Users\AliceFolder",
            userName: "AliceUser",
            exitProcess: _ => { });

        Exception thrown = CaptureThrownException(
            "Failed reading C:\\Users\\AliceFolder\\Desktop for AliceUser...\r\nSecond line...",
            new IOException("Inner denied at C:/Users/AliceFolder/file.txt by aliceuser"));

        handler.Handle(thrown, "DispatcherUnhandledException");

        Assert.NotNull(shown);
        string[] dialogLines = shown.Split('\n');
        Assert.Equal(2, dialogLines.Length);
        Assert.Equal(
            "Something went wrong: Failed reading <profile>\\Desktop for <user>... Second line. The details are in the log.",
            dialogLines[0]);
        Assert.Equal(CrashHandler.Redact(paths.LogFile, @"C:\Users\AliceFolder", "AliceUser"), dialogLines[1]);
        Assert.DoesNotContain("AliceFolder", shown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AliceUser", shown, StringComparison.OrdinalIgnoreCase);

        string[] logLines = File.ReadAllLines(paths.LogFile);
        Assert.True(logLines.Length > 3, "Expected header line plus indented exception and stack lines.");
        Assert.Contains(" FAIL DispatcherUnhandledException: Something went wrong: ", logLines[0], StringComparison.Ordinal);
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
    public void AttachDrivesAllThreeSourcesInOrderAndDetachesCleanlyOnDoubleDispose()
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

        // 2. AppDomain.UnhandledException with Exception -> logged, dialog shown, exitProcess(1).
        events.Clear();
        fakeSources.RaiseAppDomain(new InvalidOperationException("Domain boom"));
        Assert.Equal(
            ["dialog:Something went wrong: Domain boom. The details are in the log.", "exit:1"],
            events);

        // 3. AppDomain.UnhandledException with non-Exception payload -> logged, dialog shown, exitProcess(1).
        events.Clear();
        fakeSources.RaiseAppDomain("Raw string failure");
        Assert.Equal(
            ["dialog:Something went wrong: Raw string failure. The details are in the log.", "exit:1"],
            events);

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

        // 5. Double-dispose detaches all three handlers cleanly -> zero further deliveries.
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
    }

    [Fact]
    public void ReEntrantDialogThrowingDialogAndThrowingPayloadToStringStillCompleteActionsAndReleaseGuard()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);

        int dialogCalls = 0;
        int exitCalls = 0;
        int shutdownCalls = 0;
        CrashHandler? handler = null;
        bool throwInDialog = false;
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

        // 3. Guard was released after throwing dialog: a subsequent fatal event still displays a dialog.
        throwInDialog = false;
        sources.RaiseDispatcher(new InvalidOperationException("Later"), () => { });
        Assert.Equal(3, dialogCalls);
        Assert.Equal(2, shutdownCalls);

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
