using Berth.Core;

namespace Berth;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine("Berth " + AppInfo.Version);
            return 0;
        }

        return 2;
    }
}
