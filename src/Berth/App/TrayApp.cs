using Berth.Core.Model;

namespace Berth.App;

/// <summary>Tray application entry point (SPEC §17); full WPF + tray wiring lands in D04 T01 §5-§10.</summary>
internal static class TrayApp
{
    public static int Run(bool openWindow, Settings settings, Action? onWindowOpened = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = openWindow;
        _ = onWindowOpened;
        return 0;
    }
}
