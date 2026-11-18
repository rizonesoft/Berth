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

        // The mutex is created, taken and released on this one thread, as a mutex requires.
        using var mutex = new Mutex(initiallyOwned: false, args[1]);
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
                case "try":
                    held = Take(mutex, TimeSpan.Zero);
                    Console.WriteLine(held ? "acquired" : "busy");
                    break;
                case "wait":
                    held = Take(mutex, TimeSpan.FromSeconds(15));
                    Console.WriteLine(held ? "acquired" : "busy");
                    break;
                case "release":
                    if (held)
                    {
                        mutex.ReleaseMutex();
                        held = false;
                    }

                    Console.WriteLine("released");
                    break;
                case "data":
                    Console.WriteLine(Environment.GetEnvironmentVariable("BERTH_DATA") ?? "<unset>");
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

    private static bool Take(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
