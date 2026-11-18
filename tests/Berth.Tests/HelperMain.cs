namespace Berth.Tests;

/// <summary>The commands a helper process understands. Each command prints exactly one reply line.</summary>
internal static class HelperMain
{
    public static int Run(string[] args)
    {
        if (args.Length != 2 || args[0] != "mutex")
        {
            Console.Error.WriteLine("usage: --berth-helper mutex <name>");
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
