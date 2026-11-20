using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Berth.Tests;

/// <summary>The commands a helper process understands. Each command prints exactly one reply line.</summary>
internal static partial class HelperMain
{
    public static int Run(string[] args)
    {
        if (args.Length == 3 && args[0] == "log")
        {
            return RunLog(cap: long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), tag: args[2]);
        }

        if (args.Length == 1 && args[0] == "sleep-forever")
        {
            Thread.Sleep(TimeSpan.FromSeconds(60));
            return 0;
        }

        if (args.Length == 1 && args[0] == "spawn-descendant")
        {
            return RunSpawnDescendant();
        }

        if (args.Length >= 2 && args[0] == "launch-detached")
        {
            return RunLaunchDetached(args[1], args[2..]);
        }

        if (args.Length != 2 || args[0] != "mutex")
        {
            Console.Error.WriteLine("usage: --berth-helper mutex <name> | log <cap> <tag> | sleep-forever | spawn-descendant | launch-detached <exe> [args...]");
            return 2;
        }

        // The mutex is opened, taken and released on this one thread, as a mutex requires.
        using Mutex mutex = DesktopTestGate.OpenSharedMutex(args[1]);
        bool held = false;
        Console.WriteLine("ready");
        while (Console.ReadLine() is { } command)
        {
            switch (command)
            {
                case "try" or "wait" when held:
                    // A mutex can be taken again by its owner, which one release would not undo: never nest.
                    Console.WriteLine("already held");
                    break;
                case "try" or "wait":
                    string reply = Take(mutex, command == "try" ? TimeSpan.Zero : TimeSpan.FromSeconds(15));
                    held = reply != "busy";
                    Console.WriteLine(reply);
                    break;
                case "release":
                    if (held)
                    {
                        mutex.ReleaseMutex();
                        held = false;
                    }

                    Console.WriteLine("released");
                    break;
                case "env":
                    string others = string.Join(
                        ",",
                        Environment.GetEnvironmentVariables().Keys.Cast<string>()
                            .Where(k => k.StartsWith("BERTH_", StringComparison.OrdinalIgnoreCase)
                                && !k.Equals("BERTH_DATA", StringComparison.OrdinalIgnoreCase))
                            .Order(StringComparer.OrdinalIgnoreCase));
                    Console.WriteLine($"{Environment.GetEnvironmentVariable("BERTH_DATA") ?? "<unset>"}|{others}");
                    break;
                case "exit":
                    Console.WriteLine("bye");
                    return 0;
                default:
                    Console.WriteLine("unknown " + command);
                    break;
            }
        }

        return 0;
    }

    /// <summary>
    /// A second writer of the log under BERTH_DATA (as the test's <c>BerthPaths.ForRoot</c> lays it out): on
    /// <c>write &lt;n&gt;</c> it logs <c>&lt;tag&gt; &lt;i&gt;</c> for i from 1 to n and replies <c>done</c>.
    /// </summary>
    private static int RunLog(long cap, string tag)
    {
        string root = Environment.GetEnvironmentVariable("BERTH_DATA") ?? throw new InvalidOperationException("BERTH_DATA is not set.");
        var log = new Berth.Core.Log(Berth.Core.BerthPaths.ForRoot(root), TimeProvider.System, cap, afterFailedAttempt: null);
        Console.WriteLine("ready");
        while (Console.ReadLine() is { } command)
        {
            if (command.StartsWith("write ", StringComparison.Ordinal))
            {
                // Bounded: a fixture's write loop never runs on without end (AGENTS.md).
                int count = Math.Clamp(int.Parse(command[6..], System.Globalization.CultureInfo.InvariantCulture), 0, 10_000);
                for (int i = 1; i <= count; i++)
                {
                    log.Info($"{tag} {i}");
                }

                Console.WriteLine("done");
            }
            else if (command == "exit")
            {
                Console.WriteLine("bye");
                return 0;
            }
            else
            {
                Console.WriteLine("unknown " + command);
            }
        }

        return 0;
    }

    /// <summary><c>acquired</c> after an ordinary release, <c>abandoned</c> when the last owner died holding it.</summary>
    private static int RunSpawnDescendant()
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string apphost = Path.Combine(AppContext.BaseDirectory, "Berth.Tests.exe");
        if (File.Exists(apphost))
        {
            info.FileName = apphost;
        }
        else
        {
            info.FileName = Environment.ProcessPath ?? throw new InvalidOperationException("No test process path.");
            info.ArgumentList.Add(typeof(HelperMain).Assembly.Location);
        }

        info.ArgumentList.Add(HelperProcess.Switch);
        info.ArgumentList.Add("sleep-forever");

        using Process child = Process.Start(info) ?? throw new InvalidOperationException("Failed to start descendant.");
        Console.WriteLine($"child-pid={child.Id}");
        Console.Out.Flush();
        Thread.Sleep(TimeSpan.FromSeconds(60));
        return 0;
    }

    private static int RunLaunchDetached(string exePath, string[] childArgs)
    {
        // This helper must have been started with DETACHED_PROCESS so it has no console of its own, matching Explorer.
        if (NativeMethods.GetConsoleWindow() != nint.Zero)
        {
            return 91;
        }

        string commandLine = BuildCommandLine(exePath, childArgs);
        var si = new StartupInfoW
        {
            cb = Marshal.SizeOf<StartupInfoW>(),
            dwFlags = StartfUseShowWindow,
            wShowWindow = SwHide,
        };

        // Launch child WITHOUT DETACHED_PROCESS and WITHOUT CREATE_NO_WINDOW so Windows decides purely from the
        // target executable's manifest (consoleAllocationPolicy) whether to allocate a console, while SW_HIDE keeps
        // any allocated console window hidden from the desktop.
        bool ok = NativeMethods.CreateProcessW(
            exePath,
            commandLine,
            nint.Zero,
            nint.Zero,
            bInheritHandles: false,
            dwCreationFlags: CreateUnicodeEnvironment,
            lpEnvironment: nint.Zero,
            lpCurrentDirectory: Path.GetDirectoryName(exePath),
            in si,
            out ProcessInformation pi);

        if (!ok)
        {
            return 90;
        }

        try
        {
            uint wait = NativeMethods.WaitForSingleObject(pi.hProcess, 15_000);
            if (wait != WaitObject0)
            {
                try
                {
                    using Process hung = Process.GetProcessById((int)pi.dwProcessId);
                    hung.Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }

                return 92;
            }

            if (!NativeMethods.GetExitCodeProcess(pi.hProcess, out uint exitCode))
            {
                return 93;
            }

            return unchecked((int)exitCode);
        }
        finally
        {
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

    private static string BuildCommandLine(string exePath, string[] args)
    {
        var parts = new List<string>(args.Length + 1) { QuoteArg(exePath) };
        foreach (string arg in args)
        {
            parts.Add(QuoteArg(arg));
        }

        return string.Join(' ', parts);
    }

    private static string QuoteArg(string arg) =>
        arg.Length == 0 || arg.Contains(' ') || arg.Contains('"')
            ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : arg;

    private const uint StartfUseShowWindow = 0x00000001u;
    private const ushort SwHide = 0;
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
        [LibraryImport("kernel32.dll")]
        internal static partial nint GetConsoleWindow();

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

    private static string Take(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait) ? "acquired" : "busy";
        }
        catch (AbandonedMutexException)
        {
            return "abandoned";
        }
    }
}
