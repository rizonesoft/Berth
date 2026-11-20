using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Berth.Cli;
using Berth.Core;

namespace Berth.Tests.Cli;

public sealed class TestModeSwitchesTests
{
    [Fact]
    public void ApplyIgnoresReportAndHoldWhenTestModeIsOff()
    {
        using var data = new TestData();
        string reportPath = Path.Combine(data.Root, "should-not-exist.txt");
        BerthPaths nonTestPaths = BerthPaths.ForRoot(data.Root);
        Assert.False(nonTestPaths.TestMode);

        int slept = 0;
        TestModeSwitches.Apply(
            nonTestPaths,
            getenv: key => key switch
            {
                "BERTH_TEST_REPORT" => reportPath,
                "BERTH_TEST_HOLD_MS" => "2000",
                _ => null,
            },
            hasConsole: () => true,
            sleep: ms => slept += ms);

        Assert.False(File.Exists(reportPath));
        Assert.Equal(0, slept);
    }

    [Fact]
    public void ApplyWritesConsoleReportBeforeHoldingAndCapsHoldAtFiveSecondsWhenTestModeIsOn()
    {
        using var data = new TestData();
        string reportOn = Path.Combine(data.Root, "console-on.txt");
        string reportOff = Path.Combine(data.Root, "console-off.txt");

        BerthPaths testPaths = BerthPaths.FromEnvironment(
            getenv: key => key == "BERTH_DATA" ? data.Root : null,
            folder: _ => data.Root);
        Assert.True(testPaths.TestMode);

        // Order check: report file must already exist on disk when sleep is invoked, and hold > 5000 caps at 5000.
        bool reportExistedWhenSleepStarted = false;
        int sleptMs = 0;
        TestModeSwitches.Apply(
            testPaths,
            getenv: key => key switch
            {
                "BERTH_TEST_REPORT" => reportOn,
                "BERTH_TEST_HOLD_MS" => "99999",
                _ => null,
            },
            hasConsole: () => true,
            sleep: ms =>
            {
                reportExistedWhenSleepStarted = File.Exists(reportOn);
                sleptMs = ms;
            });

        Assert.True(reportExistedWhenSleepStarted);
        Assert.Equal("console=1", File.ReadAllText(reportOn));
        Assert.Equal(TestModeSwitches.MaxHoldMilliseconds, sleptMs);

        // console=0 and negative/non-numeric holds do not sleep.
        int sleptInvalid = 0;
        TestModeSwitches.Apply(
            testPaths,
            getenv: key => key switch
            {
                "BERTH_TEST_REPORT" => reportOff,
                "BERTH_TEST_HOLD_MS" => "-50",
                _ => null,
            },
            hasConsole: () => false,
            sleep: ms => sleptInvalid += ms);

        Assert.Equal("console=0", File.ReadAllText(reportOff));
        Assert.Equal(0, sleptInvalid);
        Assert.Equal(0, TestModeSwitches.ParseHoldMilliseconds(null));
        Assert.Equal(0, TestModeSwitches.ParseHoldMilliseconds("   "));
        Assert.Equal(0, TestModeSwitches.ParseHoldMilliseconds("abc"));
        Assert.Equal(0, TestModeSwitches.ParseHoldMilliseconds("0"));
        Assert.Equal(1500, TestModeSwitches.ParseHoldMilliseconds("1500"));
        Assert.Equal(5000, TestModeSwitches.ParseHoldMilliseconds("5001"));
    }
}

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed partial class ConsoleBehaviourTests
{
    [Fact]
    public void BerthProcessReadsVersionStripsInheritedVarsReservesBerthDataAndKillsDescendantTreeOnTimeoutAndDispose()
    {
        using var data = new TestData();

        // 1) Normal berth.exe --version run returns "Berth 0.1.0" and exit 0.
        BerthProcessResult version = BerthProcess.Run(data.Root, ["--version"]);
        Assert.Equal(0, version.ExitCode);
        Assert.Equal("Berth " + AppInfo.Version, version.StandardOutput.Trim());
        Assert.Equal(string.Empty, version.StandardError);

        // 2) Reserved BERTH_DATA cannot be overwritten through extraEnvironment.
        Assert.Throws<ArgumentException>(() =>
            BerthProcess.Run(data.Root, ["--version"], extraEnvironment: new Dictionary<string, string> { ["BERTH_DATA"] = @"C:\Wrong" }));

        // 3) Inherited BERTH_* variables (like BERTH_TEST_REPORT) are stripped before launching child.
        string strayReport = Path.Combine(data.Root, "stray-report.txt");
        BerthProcessResult stripped = BerthProcess.RunExecutable(
            BerthProcess.FindBerthExe(),
            ["--version"],
            data.Root,
            inheritedSeed: new Dictionary<string, string> { ["BERTH_TEST_REPORT"] = strayReport, ["BERTH_TEST_HOLD_MS"] = "5000" });
        Assert.Equal(0, stripped.ExitCode);
        Assert.False(File.Exists(strayReport));

        // 4) Real Program.Main test-mode gate: unset and whitespace BERTH_DATA ignore BERTH_TEST_REPORT and BERTH_TEST_HOLD_MS.
        foreach (string? nonTestRoot in new string?[] { null, "   " })
        {
            string ignoredReport = Path.Combine(data.Root, $"ignored-{Guid.NewGuid():N}.txt");
            BerthProcessResult res = BerthProcess.RunExecutable(
                BerthProcess.FindBerthExe(),
                ["--version"],
                dataRoot: nonTestRoot,
                extraEnvironment: new Dictionary<string, string>
                {
                    ["BERTH_TEST_REPORT"] = ignoredReport,
                    ["BERTH_TEST_HOLD_MS"] = "5000",
                },
                allowUnsetBerthData: true);
            Assert.Equal(0, res.ExitCode);
            Assert.Equal("Berth " + AppInfo.Version, res.StandardOutput.Trim());
            Assert.False(File.Exists(ignoredReport));
            Assert.True(res.Elapsed < TimeSpan.FromSeconds(3), $"Expected hold to be ignored, elapsed={res.Elapsed}.");
        }

        // 5) Tree-kill on timeout AND on early Dispose: parent spawns descendant, both die within deadline.
        (string helperExe, string[] helperArgs) = HelperInvocation("spawn-descendant");
        int descendantPidOnTimeout = -1;
        using (var timeoutParent = StartRawHelperForPid(helperExe, helperArgs, out descendantPidOnTimeout))
        {
            Assert.True(descendantPidOnTimeout > 0);
            Assert.True(IsProcessAlive(descendantPidOnTimeout));
            Assert.Throws<TimeoutException>(() => timeoutParent.WaitForExit(TimeSpan.FromMilliseconds(200)));
        }

        Assert.True(WaitForProcessExit(descendantPidOnTimeout, TimeSpan.FromSeconds(5)), $"Descendant PID {descendantPidOnTimeout} survived timeout tree-kill.");

        int descendantPidOnDispose = -1;
        using (var disposeParent = StartRawHelperForPid(helperExe, helperArgs, out descendantPidOnDispose))
        {
            Assert.True(descendantPidOnDispose > 0);
            Assert.True(IsProcessAlive(descendantPidOnDispose));
        }

        Assert.True(WaitForProcessExit(descendantPidOnDispose, TimeSpan.FromSeconds(5)), $"Descendant PID {descendantPidOnDispose} survived Dispose tree-kill.");
    }

    [Fact]
    public void CmdExeWaitsForBerthUntilHoldCompletesWhileNonWaitingControlDoesNot()
    {
        using var data = new TestData();
        string berthExe = BerthProcess.FindBerthExe();
        string reportFile = Path.Combine(data.Root, "wait-report.txt");
        string outFile = Path.Combine(data.Root, "wait-out.txt");
        string markerFile = Path.Combine(data.Root, "wait-marker.txt");

        // Positive proof: cmd.exe runs berth.exe with 1500 ms hold, then writes ERRORLEVEL to markerFile.
        // No redirected stdout/stderr pipes so pipe-draining cannot artificially delay exit.
        var info = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{berthExe}\" --version > \"{outFile}\" & echo %ERRORLEVEL% > \"{markerFile}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        StripBerthEnv(info);
        info.Environment["BERTH_DATA"] = data.Root;
        info.Environment["BERTH_TEST_REPORT"] = reportFile;
        info.Environment["BERTH_TEST_HOLD_MS"] = "1500";

        using Process cmd = Process.Start(info) ?? throw new InvalidOperationException("Failed to start cmd.exe.");
        try
        {
            // Wait for BERTH_TEST_REPORT to appear (written BEFORE the 1500 ms hold).
            Assert.True(WaitForFile(reportFile, TimeSpan.FromSeconds(10)), "Berth did not write BERTH_TEST_REPORT.");
            var afterReport = Stopwatch.StartNew();

            // While Berth is inside its 1500 ms hold, cmd.exe must NOT have created markerFile yet.
            Thread.Sleep(250);
            Assert.False(File.Exists(markerFile), "cmd.exe continued to the next command while berth.exe was still held.");
            Assert.False(cmd.HasExited, "cmd.exe exited before berth.exe finished its hold.");

            Assert.True(cmd.WaitForExit(15_000), "cmd.exe did not exit within 15s.");
            afterReport.Stop();

            Assert.Equal(0, cmd.ExitCode);
            Assert.True(afterReport.Elapsed >= TimeSpan.FromMilliseconds(1300), $"cmd.exe returned too quickly after report: {afterReport.Elapsed}.");
            Assert.True(File.Exists(markerFile));
            Assert.Equal("0", File.ReadAllText(markerFile).Trim());
            Assert.Equal("Berth " + AppInfo.Version, File.ReadAllText(outFile).Trim());
        }
        finally
        {
            HelperProcess.Stop(cmd, TimeSpan.FromSeconds(10), kill: true);
        }

        // Negative control: a deliberately non-waiting launch (`start "" /b`) writes markerFile BEFORE the hold ends.
        string nonWaitReport = Path.Combine(data.Root, "nowait-report.txt");
        string nonWaitMarker = Path.Combine(data.Root, "nowait-marker.txt");
        var nonWaitInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"start \"\" /b \"{berthExe}\" --version > nul & echo done > \"{nonWaitMarker}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        StripBerthEnv(nonWaitInfo);
        nonWaitInfo.Environment["BERTH_DATA"] = data.Root;
        nonWaitInfo.Environment["BERTH_TEST_REPORT"] = nonWaitReport;
        nonWaitInfo.Environment["BERTH_TEST_HOLD_MS"] = "1500";

        using Process nonWaitCmd = Process.Start(nonWaitInfo) ?? throw new InvalidOperationException("Failed to start non-waiting cmd.exe.");
        try
        {
            var nonWaitWatch = Stopwatch.StartNew();
            Assert.True(nonWaitCmd.WaitForExit(10_000));
            nonWaitWatch.Stop();
            Assert.True(File.Exists(nonWaitMarker), "Non-waiting control did not write marker.");
            Assert.True(nonWaitWatch.Elapsed < TimeSpan.FromMilliseconds(1200), $"Non-waiting control unexpectedly waited {nonWaitWatch.Elapsed}.");
        }
        finally
        {
            HelperProcess.Stop(nonWaitCmd, TimeSpan.FromSeconds(10), kill: true);
        }
    }

    [Fact]
    public void DetachedLaunchAllocatesNoConsoleForBerthAndAllocatesOneWhenPolicyIsStripped()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        string sourceDir = Path.GetDirectoryName(BerthProcess.FindBerthExe())!;
        string copyDir = Path.Combine(data.Root, "berth-copy");
        CopyDirectory(sourceDir, copyDir);
        string tempBerthExe = Path.Combine(copyDir, "berth.exe");
        Assert.True(File.Exists(tempBerthExe));

        // 1) Unchanged copy (with consoleAllocationPolicy=detached) launched from console-less parent -> console=0.
        string reportWithPolicy = Path.Combine(data.Root, "report-with-policy.txt");
        int exitWithPolicy = LaunchViaConsolelessHelper(tempBerthExe, ["--version"], data.Root, reportWithPolicy);
        Assert.Equal(0, exitWithPolicy);
        Assert.True(File.Exists(reportWithPolicy), "Berth did not write BERTH_TEST_REPORT for unchanged copy.");
        Assert.Equal("console=0", File.ReadAllText(reportWithPolicy).Trim());

        // 2) Strip ONLY consoleAllocationPolicy from tempBerthExe's embedded manifest using hidden Windows SDK mt.exe.
        string mtExe = FindWindowsSdkMtExe();
        string extractedManifest = Path.Combine(data.Root, "extracted.manifest");
        string strippedManifest = Path.Combine(data.Root, "stripped.manifest");
        string verifiedManifest = Path.Combine(data.Root, "verified.manifest");

        RunHiddenTool(mtExe, $"-nologo -inputresource:\"{tempBerthExe};#1\" -out:\"{extractedManifest}\"");
        XDocument doc = XDocument.Load(extractedManifest);
        List<XElement> policyElements = [.. doc.Descendants().Where(e => e.Name.LocalName == "consoleAllocationPolicy")];
        Assert.NotEmpty(policyElements);
        foreach (XElement el in policyElements)
        {
            el.Remove();
        }

        doc.Save(strippedManifest);
        RunHiddenTool(mtExe, $"-nologo -manifest \"{strippedManifest}\" -outputresource:\"{tempBerthExe};#1\"");
        RunHiddenTool(mtExe, $"-nologo -inputresource:\"{tempBerthExe};#1\" -out:\"{verifiedManifest}\"");
        XDocument verifiedDoc = XDocument.Load(verifiedManifest);
        Assert.DoesNotContain(verifiedDoc.Descendants(), e => e.Name.LocalName == "consoleAllocationPolicy");

        // 3) Stripped copy launched identically from console-less parent -> console=1.
        string reportWithoutPolicy = Path.Combine(data.Root, "report-without-policy.txt");
        int exitWithoutPolicy = LaunchViaConsolelessHelper(tempBerthExe, ["--version"], data.Root, reportWithoutPolicy);
        Assert.Equal(0, exitWithoutPolicy);
        Assert.True(File.Exists(reportWithoutPolicy), "Stripped-manifest Berth did not write BERTH_TEST_REPORT.");
        Assert.Equal("console=1", File.ReadAllText(reportWithoutPolicy).Trim());
    }

    private static (string Exe, string[] PrefixArgs) HelperInvocation(string command)
    {
        string apphost = Path.Combine(AppContext.BaseDirectory, "Berth.Tests.exe");
        if (File.Exists(apphost))
        {
            return (apphost, [HelperProcess.Switch, command]);
        }

        return (
            Environment.ProcessPath ?? throw new InvalidOperationException("No test process path."),
            [typeof(HelperMain).Assembly.Location, HelperProcess.Switch, command]);
    }

    private static BerthProcess StartRawHelperForPid(string exe, string[] args, out int childPid)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        Process raw = Process.Start(info) ?? throw new InvalidOperationException("Failed to start spawn-descendant helper.");
        string? line = null;
        Task<string?> readTask = raw.StandardOutput.ReadLineAsync();
        if (!readTask.Wait(TimeSpan.FromSeconds(10)))
        {
            HelperProcess.Stop(raw, TimeSpan.FromSeconds(5), kill: true);
            raw.Dispose();
            throw new TimeoutException("spawn-descendant helper did not report child-pid.");
        }

        line = readTask.Result;
        Assert.NotNull(line);
        Assert.StartsWith("child-pid=", line, StringComparison.Ordinal);
        childPid = int.Parse(line["child-pid=".Length..], System.Globalization.CultureInfo.InvariantCulture);

        // Wrap raw process in a tiny disposal adapter using BerthProcess via reflection-free helper? No, just wrap directly:
        return WrapExistingForTest(raw);
    }

    private static BerthProcess WrapExistingForTest(Process raw) =>
        BerthProcess.FromStartedForTest(raw);

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (!IsProcessAlive(pid))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return !IsProcessAlive(pid);
    }

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (File.Exists(path))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return File.Exists(path);
    }

    private static void StripBerthEnv(ProcessStartInfo info)
    {
        foreach (string key in info.Environment.Keys.Where(k => k.StartsWith("BERTH_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            info.Environment.Remove(key);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string FindWindowsSdkMtExe()
    {
        string kitsBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        if (Directory.Exists(kitsBin))
        {
            string? found = Directory.GetDirectories(kitsBin, "10.*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, "x64", "mt.exe"))
                .FirstOrDefault(File.Exists);
            if (found is not null)
            {
                return found;
            }
        }

        throw new FileNotFoundException("Windows SDK x64 mt.exe not found under Program Files (x86)\\Windows Kits\\10\\bin.");
    }

    private static void RunHiddenTool(string exe, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process tool = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start '{exe}'.");
        try
        {
            Task<string> stdout = tool.StandardOutput.ReadToEndAsync();
            Task<string> stderr = tool.StandardError.ReadToEndAsync();
            Assert.True(tool.WaitForExit(15_000), $"Tool '{exe}' timed out.");
            _ = Task.WhenAll(stdout, stderr).Wait(TimeSpan.FromSeconds(5));
            Assert.True(
                tool.ExitCode == 0,
                $"Tool '{exe} {arguments}' failed (exit {tool.ExitCode}): {stderr.Result} {stdout.Result}");
        }
        finally
        {
            HelperProcess.Stop(tool, TimeSpan.FromSeconds(5), kill: true);
        }
    }

    private static int LaunchViaConsolelessHelper(string targetExe, string[] targetArgs, string dataRoot, string reportPath)
    {
        (string helperExe, string[] prefixArgs) = HelperInvocation("launch-detached");
        var allArgs = new List<string>(prefixArgs) { targetExe };
        allArgs.AddRange(targetArgs);
        string cmdLine = string.Join(' ', allArgs.Prepend(helperExe).Select(QuoteArg));

        // Build clean Unicode environment block (stripping inherited BERTH_* vars, adding BERTH_DATA + BERTH_TEST_REPORT).
        var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string key = (string)entry.Key;
            if (!key.StartsWith("BERTH_", StringComparison.OrdinalIgnoreCase))
            {
                env[key] = (string?)entry.Value ?? string.Empty;
            }
        }

        env["BERTH_DATA"] = dataRoot;
        env["BERTH_TEST_REPORT"] = reportPath;

        var envBuilder = new StringBuilder();
        foreach ((string k, string v) in env)
        {
            envBuilder.Append(k).Append('=').Append(v).Append('\0');
        }

        envBuilder.Append('\0');
        byte[] envBytes = Encoding.Unicode.GetBytes(envBuilder.ToString());
        nint envBlock = Marshal.AllocHGlobal(envBytes.Length);
        try
        {
            Marshal.Copy(envBytes, 0, envBlock, envBytes.Length);
            var si = new StartupInfoW
            {
                cb = Marshal.SizeOf<StartupInfoW>(),
                dwFlags = StartfUseShowWindow,
                wShowWindow = SwHide,
            };

            bool ok = NativeMethods.CreateProcessW(
                helperExe,
                cmdLine,
                nint.Zero,
                nint.Zero,
                bInheritHandles: false,
                dwCreationFlags: DetachedProcess | CreateUnicodeEnvironment,
                lpEnvironment: envBlock,
                lpCurrentDirectory: Path.GetDirectoryName(helperExe),
                in si,
                out ProcessInformation pi);

            Assert.True(ok, $"CreateProcessW failed for console-less helper (Win32 error {Marshal.GetLastPInvokeError()}).");
            try
            {
                uint wait = NativeMethods.WaitForSingleObject(pi.hProcess, 20_000);
                if (wait != WaitObject0)
                {
                    KillTreeById((int)pi.dwProcessId);
                    throw new TimeoutException("Console-less helper did not exit within 20s.");
                }

                Assert.True(NativeMethods.GetExitCodeProcess(pi.hProcess, out uint exitCode));
                return unchecked((int)exitCode);
            }
            finally
            {
                KillTreeById((int)pi.dwProcessId);
                if (pi.hThread != nint.Zero)
                {
                    _ = NativeMethods.CloseHandle(pi.hThread);
                }

                if (pi.hProcess != nint.Zero)
                {
                    _ = NativeMethods.CloseHandle(pi.hProcess);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(envBlock);
        }
    }

    private static void KillTreeById(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                _ = p.WaitForExit(5000);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string QuoteArg(string arg) =>
        arg.Length == 0 || arg.Contains(' ') || arg.Contains('"')
            ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : arg;

    private const uint StartfUseShowWindow = 0x00000001u;
    private const ushort SwHide = 0;
    private const uint DetachedProcess = 0x00000008u;
    private const uint CreateUnicodeEnvironment = 0x00000400u;
    private const uint WaitObject0 = 0x00000000u;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoW
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CreateProcessW(
            string? lpApplicationName,
            string lpCommandLine,
            nint lpProcessAttributes,
            nint lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            nint lpEnvironment,
            string? lpCurrentDirectory,
            in StartupInfoW lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(nint hObject);
    }
}
