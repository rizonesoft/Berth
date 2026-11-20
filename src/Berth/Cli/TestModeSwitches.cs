// Windows SDK 10.0.26100.0 (Wincon.h)
// GetConsoleWindow: https://learn.microsoft.com/en-us/windows/console/getconsolewindow

using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Berth.Core;

namespace Berth.Cli;

/// <summary>
/// SPEC §14 test-mode switches (<c>BERTH_TEST_REPORT</c> and <c>BERTH_TEST_HOLD_MS</c>), honoured strictly when
/// <see cref="BerthPaths.TestMode"/> is <see langword="true"/> (i.e. <c>BERTH_DATA</c> is set).
/// </summary>
internal static partial class TestModeSwitches
{
    public const int MaxHoldMilliseconds = 5000;

    public static void Apply(
        BerthPaths paths,
        Func<string, string?>? getenv = null,
        Func<bool>? hasConsole = null,
        Action<int>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.TestMode)
        {
            return;
        }

        Func<string, string?> readEnv = getenv ?? Environment.GetEnvironmentVariable;

        // Write BERTH_TEST_REPORT before sleeping so callers testing shell-wait behaviour can observe that
        // berth.exe is running and inside its hold before the hold ends.
        string? reportPath = readEnv("BERTH_TEST_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            bool attached = (hasConsole ?? HasConsoleWindow)();
            BerthPaths.EnsureFolderFor(reportPath);
            File.WriteAllText(reportPath, attached ? "console=1" : "console=0");
        }

        int holdMs = ParseHoldMilliseconds(readEnv("BERTH_TEST_HOLD_MS"));
        if (holdMs > 0)
        {
            (sleep ?? Thread.Sleep)(holdMs);
        }
    }

    internal static int ParseHoldMilliseconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        if (!BigInteger.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger value) || value <= 0)
        {
            return 0;
        }

        return value >= MaxHoldMilliseconds ? MaxHoldMilliseconds : (int)value;
    }

    private static bool HasConsoleWindow() => NativeMethods.GetConsoleWindow() != nint.Zero;

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        internal static partial nint GetConsoleWindow();
    }
}
