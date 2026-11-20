using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Berth.Cli;
using Berth.Core;
using Microsoft.Win32.SafeHandles;

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
        using (var timeoutParent = StartRawHelperForPid(helperExe, helperArgs, out int descendantPidOnTimeout))
        {
            Assert.True(descendantPidOnTimeout > 0);
            Assert.True(IsProcessAlive(descendantPidOnTimeout));
            Assert.Throws<TimeoutException>(() => timeoutParent.WaitForExit(TimeSpan.FromMilliseconds(200)));

            // Assert BEFORE leaving the using block so Dispose cannot mask a broken timeout tree-kill.
            Assert.True(timeoutParent.HasExited, "Timed-out parent process was still alive before Dispose.");
            Assert.True(
                WaitForProcessExit(descendantPidOnTimeout, TimeSpan.FromSeconds(5)),
                $"Descendant PID {descendantPidOnTimeout} survived timeout tree-kill before Dispose.");
        }

        int directPidOnDispose = -1;
        int descendantPidOnDispose = -1;
        using (var disposeParent = StartRawHelperForPid(helperExe, helperArgs, out descendantPidOnDispose))
        {
            directPidOnDispose = disposeParent.Id;
            Assert.True(directPidOnDispose > 0);
            Assert.True(descendantPidOnDispose > 0);
            Assert.True(IsProcessAlive(directPidOnDispose));
            Assert.True(IsProcessAlive(descendantPidOnDispose));
        }

        Assert.True(WaitForProcessExit(directPidOnDispose, TimeSpan.FromSeconds(5)), $"Direct PID {directPidOnDispose} survived Dispose tree-kill.");
        Assert.True(WaitForProcessExit(descendantPidOnDispose, TimeSpan.FromSeconds(5)), $"Descendant PID {descendantPidOnDispose} survived Dispose tree-kill.");
    }

    [Fact]
    public void InteractiveTerminalInheritsConsolePrintsVersionWaitsForHoldAndCapturesLiveExitCode()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        string berthExe = BerthProcess.FindBerthExe();
        string reportFile = Path.Combine(data.Root, "pty-report.txt");
        string markerFile = Path.Combine(data.Root, "pty-marker.txt");
        string nonzeroMarker = Path.Combine(data.Root, "pty-nonzero.txt");

        // Positive proof: drive a real interactive cmd.exe (/q /v:on) inside a hidden Windows ConPTY pseudoconsole.
        // berth.exe inherits that terminal (console=1), prints "Berth 0.1.0" directly to the terminal, holds 1500 ms
        // blocking the next command, and delayed !ERRORLEVEL! captures both 0 and nonzero (2) exit codes.
        using (var pty = InteractiveCmdSession.Start(
            data.Root,
            new Dictionary<string, string>
            {
                ["BERTH_TEST_REPORT"] = reportFile,
                ["BERTH_TEST_HOLD_MS"] = "1500",
            }))
        {
            pty.SendLine($"\"{berthExe}\" --version & echo EXIT=!ERRORLEVEL!> \"{markerFile}\"");

            // Wait for BERTH_TEST_REPORT (written BEFORE the 1500 ms hold) to witness Berth alive inside its hold.
            Assert.True(WaitForFile(reportFile, TimeSpan.FromSeconds(10)), "Berth did not write BERTH_TEST_REPORT in interactive terminal.");
            var afterReport = Stopwatch.StartNew();
            Assert.Equal("console=1", File.ReadAllText(reportFile).Trim());

            // While Berth is inside its 1500 ms hold, cmd.exe must NOT have reached the marker command yet.
            Thread.Sleep(250);
            Assert.False(File.Exists(markerFile), "Interactive cmd.exe continued before berth.exe finished its hold.");

            Assert.True(WaitForFile(markerFile, TimeSpan.FromSeconds(10)), "Interactive cmd.exe did not write exit marker after Berth exited.");
            afterReport.Stop();
            Assert.True(afterReport.Elapsed >= TimeSpan.FromMilliseconds(1400), $"Interactive cmd.exe returned too quickly after report: {afterReport.Elapsed}.");
            Assert.Equal("EXIT=0", File.ReadAllText(markerFile).Trim());

            // Verify !ERRORLEVEL! genuinely captures a nonzero exit code from berth.exe (unknown arg -> exit 2).
            pty.SendLine($"set BERTH_TEST_HOLD_MS=0 & \"{berthExe}\" --unknown & echo EXIT=!ERRORLEVEL!> \"{nonzeroMarker}\"");
            Assert.True(WaitForFile(nonzeroMarker, TimeSpan.FromSeconds(10)), "Interactive cmd.exe did not write nonzero marker.");
            Assert.Equal("EXIT=2", File.ReadAllText(nonzeroMarker).Trim());

            string terminalOutput = pty.ExitAndReadAll(TimeSpan.FromSeconds(10));
            Assert.Contains("Berth " + AppInfo.Version, terminalOutput, StringComparison.Ordinal);
        }

        // Negative control: a deliberately non-waiting launch (`start "" /b`) inside a dedicated Job Object writes
        // nonWaitMarker while berth.exe is still alive inside its 1500 ms hold, writes nonWaitReport, and exits 0;
        // the Job Object retains ownership of every child process and terminates any survivor on failure or disposal.
        string nonWaitReport = Path.Combine(data.Root, "nowait-report.txt");
        string nonWaitMarker = Path.Combine(data.Root, "nowait-marker.txt");
        string nonWaitCmdLine = $"cmd.exe /c \"start \"\" /b \"{berthExe}\" --version > nul & echo done > \"{nonWaitMarker}\"\"";
        using var job = JobTrackedProcess.StartHidden(
            nonWaitCmdLine,
            data.Root,
            new Dictionary<string, string>
            {
                ["BERTH_TEST_REPORT"] = nonWaitReport,
                ["BERTH_TEST_HOLD_MS"] = "1500",
            });

        var nonWaitWatch = Stopwatch.StartNew();
        Assert.True(job.WaitForRootExit(TimeSpan.FromSeconds(10)), "Non-waiting cmd.exe did not exit in time.");
        nonWaitWatch.Stop();
        Assert.True(File.Exists(nonWaitMarker), "Non-waiting control did not write marker.");
        Assert.True(nonWaitWatch.Elapsed < TimeSpan.FromMilliseconds(1200), $"Non-waiting control unexpectedly waited {nonWaitWatch.Elapsed}.");

        // Confirm berth.exe actually launched inside the job and observe it still alive AFTER the marker was written.
        int berthChildPid = job.WaitForChildPid(TimeSpan.FromSeconds(5));
        Assert.True(WaitForFile(nonWaitReport, TimeSpan.FromSeconds(5)), "Non-waiting control did not write BERTH_TEST_REPORT.");
        using Process berthChild = Process.GetProcessById(berthChildPid);
        nint berthChildHandle = berthChild.Handle;
        Assert.False(berthChild.HasExited, "Non-waiting control's berth.exe child had already exited before its hold finished.");

        // Wait for berth.exe to finish its hold and exit cleanly with code 0 before TestData disposal.
        Assert.True(berthChild.WaitForExit(10_000), "Non-waiting control's berth.exe child did not exit within deadline.");
        Assert.True(NativeMethods.GetExitCodeProcess(berthChildHandle, out uint childExitCode));
        Assert.Equal(0u, childExitCode);
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
        try
        {
            Task<string?> readTask = raw.StandardOutput.ReadLineAsync();
            if (!readTask.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("spawn-descendant helper did not report child-pid.");
            }

            string? line = readTask.Result;
            Assert.NotNull(line);
            Assert.StartsWith("child-pid=", line, StringComparison.Ordinal);
            childPid = int.Parse(line["child-pid=".Length..], System.Globalization.CultureInfo.InvariantCulture);

            return BerthProcess.FromStartedForTest(raw);
        }
        catch
        {
            HelperProcess.Stop(raw, TimeSpan.FromSeconds(5), kill: true);
            raw.Dispose();
            throw;
        }
    }

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

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
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
            Assert.True(Task.WhenAll(stdout, stderr).Wait(TimeSpan.FromSeconds(5)), $"Tool '{exe}' stdout/stderr did not finish draining within 5s.");
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
    private const uint StartfUseStdHandles = 0x00000100u;
    private const ushort SwHide = 0;
    private const uint DetachedProcess = 0x00000008u;
    private const uint CreateUnicodeEnvironment = 0x00000400u;
    private const uint ExtendedStartupinfoPresent = 0x00080000u;
    private const uint WaitObject0 = 0x00000000u;
    private static readonly nint ProcThreadAttributePseudoConsole = new(0x00020016);

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoExW
    {
        public StartupInfoW StartupInfo;
        public nint lpAttributeList;
    }

    /// <summary>
    /// Hosts a real interactive <c>cmd.exe /q /v:on</c> inside a hidden Windows ConPTY pseudoconsole so tests can
    /// prove terminal inheritance, terminal output, prompt blocking, and delayed <c>!ERRORLEVEL!</c> capture without
    /// opening a visible window on screen.
    /// </summary>
    private sealed class InteractiveCmdSession : IDisposable
    {
        private nint _hpc;
        private readonly nint _hProcess;
        private readonly nint _hThread;
        private readonly int _pid;
        private readonly FileStream _inputStream;
        private readonly StreamWriter _writer;
        private readonly FileStream _outputStream;
        private readonly Task<string> _outputTask;
        private bool _disposed;

        private InteractiveCmdSession(
            nint hpc,
            nint hProcess,
            nint hThread,
            int pid,
            SafeFileHandle writeToPty,
            SafeFileHandle readFromPty)
        {
            _hpc = hpc;
            _hProcess = hProcess;
            _hThread = hThread;
            _pid = pid;
            _inputStream = new FileStream(writeToPty, FileAccess.Write, bufferSize: 4096, isAsync: false);
            _writer = new StreamWriter(_inputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            _outputStream = new FileStream(readFromPty, FileAccess.Read, bufferSize: 4096, isAsync: false);
            _outputTask = Task.Run(() =>
            {
                using var reader = new StreamReader(_outputStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                return reader.ReadToEnd();
            });
        }

        public static InteractiveCmdSession Start(string dataRoot, IReadOnlyDictionary<string, string> extraEnv)
        {
            Assert.True(NativeMethods.CreatePipe(out SafeFileHandle ptyInputRead, out SafeFileHandle ptyInputWrite, nint.Zero, 0));
            Assert.True(NativeMethods.CreatePipe(out SafeFileHandle ptyOutputRead, out SafeFileHandle ptyOutputWrite, nint.Zero, 0));

            nint hpc = nint.Zero;
            nint attrList = nint.Zero;
            bool attrListInitialized = false;
            nint envBlock = nint.Zero;

            try
            {
                var size = new Coord { X = 120, Y = 30 };
                int hr = NativeMethods.CreatePseudoConsole(size, ptyInputRead.DangerousGetHandle(), ptyOutputWrite.DangerousGetHandle(), 0, out hpc);
                Assert.True(hr == 0 && hpc != nint.Zero, $"CreatePseudoConsole failed with HRESULT 0x{hr:X8}.");

                // Close the ConPTY-side ends now that ConPTY duplicated them.
                ptyInputRead.Dispose();
                ptyOutputWrite.Dispose();

                nint attrSize = nint.Zero;
                _ = NativeMethods.InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref attrSize);
                attrList = Marshal.AllocHGlobal(attrSize);
                Assert.True(NativeMethods.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize));
                attrListInitialized = true;

                Assert.True(NativeMethods.UpdateProcThreadAttribute(
                    attrList,
                    0,
                    ProcThreadAttributePseudoConsole,
                    hpc,
                    (nint)nint.Size,
                    nint.Zero,
                    nint.Zero));

                envBlock = BuildEnvironmentBlock(dataRoot, extraEnv);
                var siEx = new StartupInfoExW
                {
                    StartupInfo = new StartupInfoW
                    {
                        cb = Marshal.SizeOf<StartupInfoExW>(),
                        dwFlags = StartfUseStdHandles,
                    },
                    lpAttributeList = attrList,
                };

                string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                bool ok = NativeMethods.CreateProcessWEx(
                    cmdExe,
                    $"\"{cmdExe}\" /q /v:on",
                    nint.Zero,
                    nint.Zero,
                    bInheritHandles: false,
                    dwCreationFlags: ExtendedStartupinfoPresent | CreateUnicodeEnvironment,
                    lpEnvironment: envBlock,
                    lpCurrentDirectory: dataRoot,
                    in siEx,
                    out ProcessInformation pi);

                Assert.True(ok, $"CreateProcessWEx for ConPTY cmd.exe failed (Win32 error {Marshal.GetLastPInvokeError()}).");

                var session = new InteractiveCmdSession(hpc, pi.hProcess, pi.hThread, (int)pi.dwProcessId, ptyInputWrite, ptyOutputRead);
                hpc = nint.Zero;
                ptyInputWrite = null!;
                ptyOutputRead = null!;
                return session;
            }
            catch
            {
                ptyInputRead?.Dispose();
                ptyInputWrite?.Dispose();
                ptyOutputRead?.Dispose();
                ptyOutputWrite?.Dispose();
                if (hpc != nint.Zero)
                {
                    NativeMethods.ClosePseudoConsole(hpc);
                }

                throw;
            }
            finally
            {
                if (attrListInitialized)
                {
                    NativeMethods.DeleteProcThreadAttributeList(attrList);
                }

                if (attrList != nint.Zero)
                {
                    Marshal.FreeHGlobal(attrList);
                }

                if (envBlock != nint.Zero)
                {
                    Marshal.FreeHGlobal(envBlock);
                }
            }
        }

        public void SendLine(string command)
        {
            _writer.Write(command + "\r\n");
            _writer.Flush();
        }

        public string ExitAndReadAll(TimeSpan timeout)
        {
            SendLine("exit");
            uint wait = NativeMethods.WaitForSingleObject(_hProcess, (uint)timeout.TotalMilliseconds);
            Assert.True(wait == WaitObject0, "Interactive cmd.exe did not exit cleanly after 'exit'.");

            // Closing the pseudoconsole once closes the output pipe so ReadToEnd completes deterministically.
            ClosePtyOnce();
            Assert.True(_outputTask.Wait(TimeSpan.FromSeconds(5)), "ConPTY output stream did not finish draining.");
            return _outputTask.Result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            KillTreeById(_pid);
            ClosePtyOnce();

            _writer.Dispose();
            _inputStream.Dispose();
            _outputStream.Dispose();
            if (_hThread != nint.Zero)
            {
                _ = NativeMethods.CloseHandle(_hThread);
            }

            if (_hProcess != nint.Zero)
            {
                _ = NativeMethods.CloseHandle(_hProcess);
            }
        }

        private void ClosePtyOnce()
        {
            nint hpc = Interlocked.Exchange(ref _hpc, nint.Zero);
            if (hpc != nint.Zero)
            {
                NativeMethods.ClosePseudoConsole(hpc);
            }
        }

        internal static nint BuildEnvironmentBlock(string dataRoot, IReadOnlyDictionary<string, string> extraEnv)
        {
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
            foreach ((string k, string v) in extraEnv)
            {
                env[k] = v;
            }

            var sb = new StringBuilder();
            foreach ((string k, string v) in env)
            {
                sb.Append(k).Append('=').Append(v).Append('\0');
            }

            sb.Append('\0');
            byte[] bytes = Encoding.Unicode.GetBytes(sb.ToString());
            nint block = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, block, bytes.Length);
            return block;
        }
    }

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

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            nint lpPipeAttributes,
            uint nSize);

        [LibraryImport("kernel32.dll")]
        internal static partial int CreatePseudoConsole(
            Coord size,
            nint hInput,
            nint hOutput,
            uint dwFlags,
            out nint phPC);

        [LibraryImport("kernel32.dll")]
        internal static partial void ClosePseudoConsole(nint hPC);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool InitializeProcThreadAttributeList(
            nint lpAttributeList,
            uint dwAttributeCount,
            uint dwFlags,
            ref nint lpSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UpdateProcThreadAttribute(
            nint lpAttributeList,
            uint dwFlags,
            nint attribute,
            nint lpValue,
            nint cbSize,
            nint lpPreviousValue,
            nint lpReturnSize);

        [LibraryImport("kernel32.dll")]
        internal static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CreateProcessWEx(
            string? lpApplicationName,
            string lpCommandLine,
            nint lpProcessAttributes,
            nint lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            nint lpEnvironment,
            string? lpCurrentDirectory,
            in StartupInfoExW lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(nint hJob, nint hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryInformationJobObject(
            nint hJob,
            int jobObjectInformationClass,
            nint lpJobObjectInformation,
            uint cbJobObjectInformationLength,
            out uint lpReturnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateJobObject(nint hJob, uint uExitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint ResumeThread(nint hThread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateProcess(nint hProcess, uint uExitCode);
    }

    private sealed class JobTrackedProcess : IDisposable
    {
        private const uint CreateNoWindow = 0x08000000u;
        private const uint CreateSuspended = 0x00000004u;
        private const uint CreateUnicodeEnvironment = 0x00000400u;
        private const uint StartfUseShowWindow = 0x00000001u;
        private const ushort SwHide = 0;
        private const uint WaitObject0 = 0x00000000u;
        private const int JobObjectBasicProcessIdList = 3;

        private nint _jobHandle;
        private nint _rootProcessHandle;
        private readonly int _rootPid;

        private JobTrackedProcess(nint jobHandle, nint rootProcessHandle, int rootPid)
        {
            _jobHandle = jobHandle;
            _rootProcessHandle = rootProcessHandle;
            _rootPid = rootPid;
        }

        public static JobTrackedProcess StartHidden(string commandLine, string dataRoot, Dictionary<string, string> extraEnv)
        {
            nint job = NativeMethods.CreateJobObjectW(0, null);
            if (job == 0)
            {
                throw new InvalidOperationException($"CreateJobObjectW failed (Win32 error {Marshal.GetLastPInvokeError()}).");
            }

            nint envBlock = InteractiveCmdSession.BuildEnvironmentBlock(dataRoot, extraEnv);
            nint hProcess = 0;
            nint hThread = 0;
            try
            {
                var startup = new StartupInfoW
                {
                    cb = Marshal.SizeOf<StartupInfoW>(),
                    dwFlags = StartfUseShowWindow,
                    wShowWindow = SwHide,
                };

                if (!NativeMethods.CreateProcessW(
                    lpApplicationName: null,
                    lpCommandLine: commandLine,
                    lpProcessAttributes: 0,
                    lpThreadAttributes: 0,
                    bInheritHandles: false,
                    dwCreationFlags: CreateNoWindow | CreateSuspended | CreateUnicodeEnvironment,
                    lpEnvironment: envBlock,
                    lpCurrentDirectory: null,
                    in startup,
                    out ProcessInformation pi))
                {
                    throw new InvalidOperationException($"CreateProcessW failed (Win32 error {Marshal.GetLastPInvokeError()}).");
                }

                hProcess = pi.hProcess;
                hThread = pi.hThread;

                if (!NativeMethods.AssignProcessToJobObject(job, hProcess))
                {
                    throw new InvalidOperationException($"AssignProcessToJobObject failed (Win32 error {Marshal.GetLastPInvokeError()}).");
                }

                if (NativeMethods.ResumeThread(hThread) == uint.MaxValue)
                {
                    throw new InvalidOperationException($"ResumeThread failed (Win32 error {Marshal.GetLastPInvokeError()}).");
                }

                NativeMethods.CloseHandle(hThread);
                hThread = 0;
                nint transferredProcess = hProcess;
                hProcess = 0;
                nint transferredJob = job;
                job = 0;
                return new JobTrackedProcess(transferredJob, transferredProcess, (int)pi.dwProcessId);
            }
            finally
            {
                Marshal.FreeHGlobal(envBlock);
                if (hThread != 0)
                {
                    NativeMethods.CloseHandle(hThread);
                }

                if (hProcess != 0)
                {
                    _ = NativeMethods.TerminateProcess(hProcess, 1);
                    NativeMethods.CloseHandle(hProcess);
                }

                if (job != 0)
                {
                    _ = NativeMethods.TerminateJobObject(job, 1);
                    NativeMethods.CloseHandle(job);
                }
            }
        }

        public bool WaitForRootExit(TimeSpan timeout) =>
            NativeMethods.WaitForSingleObject(_rootProcessHandle, (uint)timeout.TotalMilliseconds) == WaitObject0;

        public int WaitForChildPid(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            nint buffer = Marshal.AllocHGlobal(512);
            try
            {
                while (sw.Elapsed < timeout)
                {
                    if (NativeMethods.QueryInformationJobObject(_jobHandle, JobObjectBasicProcessIdList, buffer, 512, out _))
                    {
                        uint count = (uint)Marshal.ReadInt32(buffer, 4);
                        for (int i = 0; i < count; i++)
                        {
                            int pid = (int)Marshal.ReadIntPtr(buffer, 8 + (i * IntPtr.Size));
                            if (pid > 0 && pid != _rootPid)
                            {
                                return pid;
                            }
                        }
                    }

                    Thread.Sleep(20);
                }

                throw new TimeoutException($"No child process appeared in Job Object within {timeout}.");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            if (_jobHandle != 0)
            {
                _ = NativeMethods.TerminateJobObject(_jobHandle, 1);
                NativeMethods.CloseHandle(_jobHandle);
                _jobHandle = 0;
            }

            if (_rootProcessHandle != 0)
            {
                NativeMethods.CloseHandle(_rootProcessHandle);
                _rootProcessHandle = 0;
            }
        }
    }
}
