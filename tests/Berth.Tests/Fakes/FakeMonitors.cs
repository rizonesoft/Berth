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

    /// <summary>When true, <see cref="Read"/> throws <see cref="DesktopUnavailableException"/>.</summary>
    public bool Unavailable { get; set; }

    public IReadOnlyList<MonitorInfo> Read() =>
        Unavailable ? throw new DesktopUnavailableException("No monitor can be read.") : [.. Monitors];
}
