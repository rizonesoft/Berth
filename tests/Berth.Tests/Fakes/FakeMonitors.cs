using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Fakes;

/// <summary>The monitors for rule tests: a settable list, or unavailable as in a disconnected session.</summary>
internal sealed class FakeMonitors : IMonitors
{
    public FakeMonitors(params MonitorInfo[] monitors)
    {
        Monitors = [.. monitors];
    }

    public List<MonitorInfo> Monitors { get; set; }

    /// <summary>How many times the monitors were read.</summary>
    public int Reads { get; private set; }

    /// <summary>When true, <see cref="IMonitors.Read"/> throws <see cref="DesktopUnavailableException"/>, as it does for an empty list.</summary>
    public bool Unavailable { get; set; }

    IReadOnlyList<MonitorInfo> IMonitors.Read()
    {
        Reads++;
        return Unavailable || Monitors.Count == 0 ? throw new DesktopUnavailableException("No monitor can be read.") : [.. Monitors];
    }
}
