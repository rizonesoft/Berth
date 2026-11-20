using Berth.Cli;
using Berth.Core;

namespace Berth;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        BerthPaths paths = BerthPaths.FromEnvironment();
        int exitCode;
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine("Berth " + AppInfo.Version);
            exitCode = 0;
        }
        else
        {
            exitCode = 2;
        }

        TestModeSwitches.Apply(paths);
        return exitCode;
    }
}
