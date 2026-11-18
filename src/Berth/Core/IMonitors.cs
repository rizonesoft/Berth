using Berth.Core.Model;

namespace Berth.Core;

/// <summary>The monitors of the current arrangement (foundation plan, "Seams"); no Windows type appears here.</summary>
internal interface IMonitors
{
    /// <summary>Every monitor, in device pixels. Never empty.</summary>
    /// <exception cref="DesktopUnavailableException">No monitor can be read (a disconnected session).</exception>
    IReadOnlyList<MonitorInfo> Read();
}
