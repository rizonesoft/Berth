namespace Berth.Tests;

/// <summary>The commands a helper process understands. Each command prints exactly one reply line.</summary>
internal static class HelperMain
{
    public static int Run(string[] args)
    {
        if (args.Length == 3 && args[0] == "log")
        {
            return RunLog(cap: long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), tag: args[2]);
        }

        if (args.Length != 2 || args[0] != "mutex")
        {
            Console.Error.WriteLine("usage: --berth-helper mutex <name> | log <cap> <tag>");
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
