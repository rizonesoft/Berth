using System.ComponentModel;
using System.Runtime.InteropServices;
using Berth.App;

namespace Berth.Tests.App;

[Collection(DesktopCollectionDefinition.Name)]
public sealed partial class SystemEventsTests(AppFixture appFixture)
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint SmtoAbortIfHung = 0x0002;

    [Fact]
    public void CreatesHiddenUnownedTopLevelWindowWithNoTaskbarButtonAndDestroysOnDispose()
    {
        nint savedHandle = 0;
        bool isWindowBefore = false;
        bool isVisible = true;
        nint parentHandle = new(-1);
        nint exStyle = 0;

        appFixture.Invoke(() =>
        {
            using var events = new SystemEvents();
            savedHandle = events.Handle;
            isWindowBefore = IsWindow(savedHandle);
            isVisible = IsWindowVisible(savedHandle);
            parentHandle = GetParent(savedHandle);
            exStyle = GetWindowLongPtrW(savedHandle, GwlExStyle);
            events.Dispose();
            // Second Dispose must be idempotent.
            events.Dispose();
        });

        Assert.NotEqual(0, savedHandle);
        Assert.True(isWindowBefore);
        Assert.False(isVisible);
        Assert.Equal(0, parentHandle);
        Assert.NotEqual(0, exStyle & WsExToolWindow);
        Assert.False(IsWindow(savedHandle), $"Expected saved HWND 0x{savedHandle:X} to be destroyed after Dispose.");
    }

    [Fact]
    public void RegisterWindowMessageFailureThrowsWin32ExceptionWithoutAllocatingWindow()
    {
        appFixture.Invoke(() =>
        {
            Assert.Throws<Win32Exception>(() => new SystemEvents(_ => 0u));
        });
    }

    [Fact]
    public void PostedDisplayChangeAndTaskbarCreatedRaiseEvents()
    {
        using var displayFired = new ManualResetEventSlim(false);
        using var explorerFired = new ManualResetEventSlim(false);
        SystemEvents? events = null;
        nint handle = 0;

        try
        {
            appFixture.Invoke(() =>
            {
                events = new SystemEvents();
                handle = events.Handle;
                events.DisplayChanged += () => displayFired.Set();
                events.ExplorerRestarted += () => explorerFired.Set();
            });

            Assert.NotEqual(0, handle);
            Assert.True(PostMessageW(handle, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero));
            Assert.True(displayFired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "WM_DISPLAYCHANGE did not raise DisplayChanged.");

            uint taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            Assert.NotEqual(0u, taskbarCreated);
            Assert.True(PostMessageW(handle, taskbarCreated, IntPtr.Zero, IntPtr.Zero));
            Assert.True(explorerFired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "TaskbarCreated did not raise ExplorerRestarted.");
        }
        finally
        {
            if (events is not null)
            {
                appFixture.Invoke(events.Dispose);
            }
        }
    }

    [Fact]
    public async Task SynchronousSettingChangeFromWorkerThreadDeliversAreaStringWithinTwoSecondBound()
    {
        using var receivedEvent = new ManualResetEventSlim(false);
        string? receivedArea = null;
        SystemEvents? events = null;
        nint handle = 0;

        try
        {
            appFixture.Invoke(() =>
            {
                events = new SystemEvents();
                handle = events.Handle;
                events.SettingChanged += area =>
                {
                    receivedArea = area;
                    receivedEvent.Set();
                };
            });

            // Send synchronously from a separate thread so SendMessageTimeoutW genuinely enforces the 2-second bound
            // while AppFixture's STA dispatcher pumps messages.
            bool sent = await Task.Run(
                () => SendSettingChange(handle, "ImmersiveColorSet", 2000),
                TestContext.Current.CancellationToken);
            Assert.True(sent, "SendMessageTimeoutW failed or timed out for WM_SETTINGCHANGE.");
            Assert.True(receivedEvent.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.Equal("ImmersiveColorSet", receivedArea);
        }
        finally
        {
            if (events is not null)
            {
                appFixture.Invoke(events.Dispose);
            }
        }
    }

    [Fact]
    public void TrayAppRefreshesTooltipOnDisplayChangeSwitchesThemesOnSettingChangeAndDestroysHwndOnShutdown()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "system-events-report.txt");
        string instanceName = $"test-sysevents-{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.SystemEventsTrayWiringScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Isolated child exited {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

        Assert.Equal(string.Empty, report["workerError"]);
        Assert.Equal("True", report["savedHandleNonZero"]);
        Assert.Contains("Laptop", report["initialTooltip"], StringComparison.Ordinal);
        Assert.Contains("MidStartup", report["tooltipAfterStartupReconcile"], StringComparison.Ordinal);
        Assert.Contains("Docked", report["tooltipAfterDisplayChange"], StringComparison.Ordinal);
        Assert.Contains("Docked", report["tooltipAfterStaleMenuCompletion"], StringComparison.Ordinal);
        Assert.Equal("Dark", report["initialAppTheme"]);
        Assert.Equal("True", report["sendSettingResult"]);
        Assert.Equal("Light", report["appThemeAfterSettingChange"]);
        Assert.Equal("True", report["hasLightDictAfterSettingChange"]);
        Assert.Equal("True", report["iconHandleChanged"]);
        Assert.Equal("True", report["taskbarIsLightAfterSettingChange"]);
        Assert.Equal("False", report["isWindowAfterShutdown"]);
        Assert.Equal("False", report["menuBarrierTimedOut"]);
    }

    internal static unsafe bool SendSettingChange(nint hwnd, string area, uint timeoutMs)
    {
        // Pin the null-terminated UTF-16 buffer for the full duration of SendMessageTimeoutW.
        fixed (char* ptr = area)
        {
            nint result = SendMessageTimeoutW(
                hwnd,
                SystemEvents.WmSettingChange,
                IntPtr.Zero,
                (IntPtr)ptr,
                SmtoAbortIfHung,
                timeoutMs,
                out _);
            return result != 0;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetParent")]
    private static partial nint GetParent(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static partial nint SendMessageTimeoutW(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam,
        uint fuFlags,
        uint uTimeout,
        out nint lpdwResult);
}
