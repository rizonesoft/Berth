namespace Berth.Tests;

/// <summary>
/// The test assembly's entry point. It runs the tests exactly as xUnit's generated entry point does, and with
/// <c>--berth-helper</c> as its first argument it runs a helper that a test drives from another process.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == HelperProcess.Switch)
        {
            return HelperMain.Run(args[1..]);
        }

        if (args.Length > 0 && args[0] == App.IsolatedStaChildRunner.Switch)
        {
            return App.IsolatedStaChildRunner.RunMain(args[1..]);
        }

        if (args.Any(arg => arg == "--server" || arg == "--internal-msbuild-node"))
        {
            return Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework
                .RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult();
        }

        return Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }
}
