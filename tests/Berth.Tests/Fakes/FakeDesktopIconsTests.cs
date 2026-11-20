using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Fakes;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "The fakes are exercised through the interfaces the rules use, on purpose.")]
public sealed class FakeDesktopIconsTests
{
    private static readonly IconPosition A = new(@"C:\Desk\a.txt", "Notes", 10, 20);
    private static readonly IconPosition B = new(@"C:\Desk\b.txt", "Notes", 30, 40);
    private static readonly IconPosition C = new(@"C:\Desk\c.txt", "c.txt", 50, 60);

    // Every behaviour is exercised through the interfaces the rules use, not the fakes' own types.
    private static IDesktopIcons Seam(FakeDesktopIcons fake) => fake;

    private static IMonitors Seam(FakeMonitors fake) => fake;

    [Fact]
    public void ASetPositionIsReadBack()
    {
        IDesktopIcons desktop = Seam(new FakeDesktopIcons(A, B));

        desktop.SetPositions([A with { X = 100, Y = 200 }]);

        Assert.Equal([A with { X = 100, Y = 200 }, B], desktop.ReadIcons());
    }

    [Fact]
    public void AnUnknownIdIsIgnoredEvenWhenItSharesALiveIconsName()
    {
        IDesktopIcons desktop = Seam(new FakeDesktopIcons(A, B));

        desktop.SetPositions([new IconPosition(@"C:\Desk\gone.txt", "Notes", 500, 500)]);

        Assert.Equal([A, B], desktop.ReadIcons());
    }

    [Fact]
    public void AMoveMatchesTheIdIgnoringCaseAndChangesOnlyItsCoordinates()
    {
        IDesktopIcons desktop = Seam(new FakeDesktopIcons(A, B));

        // The same display name as both live icons, a stale name in the request, and a differently cased id.
        desktop.SetPositions([new IconPosition(@"c:\DESK\A.TXT", "Renamed", 7, 8)]);

        Assert.Equal([A with { X = 7, Y = 8 }, B], desktop.ReadIcons());
    }

    [Fact]
    public void AutoArrangeTurnsOffWhenWindowsAllowsIt()
    {
        var fake = new FakeDesktopIcons(A);
        fake.SetAutoArrange(true);
        IDesktopIcons desktop = Seam(fake);

        Assert.True(desktop.TryTurnAutoArrangeOff());
        Assert.False(desktop.AutoArrange);
    }

    [Fact]
    public void AutoArrangeStaysOnWhenWindowsKeepsIt()
    {
        var fake = new FakeDesktopIcons(A) { KeepsAutoArrange = true };
        fake.SetAutoArrange(true);
        IDesktopIcons desktop = Seam(fake);

        Assert.False(desktop.TryTurnAutoArrangeOff());
        Assert.True(desktop.AutoArrange);
    }

    [Fact]
    public void SnapByShiftsOnlyTheChosenIcons()
    {
        IconPosition[] wanted = [A with { X = 100 }, B with { X = 200 }, C with { X = 300 }];

        var exact = new FakeDesktopIcons(A, B, C);
        Seam(exact).SetPositions(wanted);
        Assert.Equal(wanted, Seam(exact).ReadIcons());

        var one = new FakeDesktopIcons(A, B, C) { SnapBy = 4, SnapIds = new HashSet<string>([@"C:\DESK\B.TXT"]) };
        Seam(one).SetPositions(wanted);
        Assert.Equal([wanted[0], wanted[1] with { X = 204, Y = 44 }, wanted[2]], Seam(one).ReadIcons());

        var all = new FakeDesktopIcons(A, B, C) { SnapBy = 4 };
        Seam(all).SetPositions(wanted);
        Assert.Equal([wanted[0] with { X = 104, Y = 24 }, wanted[1] with { X = 204, Y = 44 }, wanted[2] with { X = 304, Y = 64 }], Seam(all).ReadIcons());
    }

    [Fact]
    public void EveryCallIsRecordedEvenOneThatMovesNothing()
    {
        var fake = new FakeDesktopIcons(A);
        IDesktopIcons desktop = Seam(fake);

        desktop.SetPositions([A]);
        desktop.SetPositions([new IconPosition(@"C:\Desk\gone.txt", "gone.txt", 1, 1)]);

        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal([A], fake.Calls[0]);
        Assert.Equal(@"C:\Desk\gone.txt", Assert.Single(fake.Calls[1]).Id);
        Assert.Equal([A], desktop.ReadIcons());
    }

    [Fact]
    public void ARecordedCallKeepsWhatWasAskedEvenIfTheCallersListChanges()
    {
        var fake = new FakeDesktopIcons(A);
        IconPosition[] request = [A with { X = 5 }];

        Seam(fake).SetPositions(request);
        request[0] = A with { X = 999 };

        Assert.Equal([A with { X = 5 }], Assert.Single(fake.Calls));
    }

    [Fact]
    public void AnUnavailableDesktopThrowsFromEveryMemberAndChangesNothing()
    {
        var fake = new FakeDesktopIcons(A);
        fake.SetAutoArrange(true);
        fake.Unavailable = true;
        IDesktopIcons desktop = Seam(fake);

        Assert.Throws<DesktopUnavailableException>(() => desktop.ReadIcons());
        Assert.Throws<DesktopUnavailableException>(() => desktop.SetPositions([A with { X = 99 }]));
        Assert.Throws<DesktopUnavailableException>(() => desktop.AutoArrange);
        Assert.Throws<DesktopUnavailableException>(() => desktop.TryTurnAutoArrangeOff());

        fake.Unavailable = false;
        Assert.Equal([A], desktop.ReadIcons());
        Assert.True(desktop.AutoArrange);

        // The refused write is still recorded as attempted.
        Assert.Equal([A with { X = 99 }], Assert.Single(fake.Calls));
    }

    [Fact]
    public void TheExceptionKeepsItsCause()
    {
        var cause = new InvalidOperationException("Explorer is not running.");

        var error = new DesktopUnavailableException("The desktop is unavailable.", cause);

        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void MonitorsAreReadBackAsSet()
    {
        var one = new MonitorInfo(0, 0, 2560, 1440, 100, true);
        var left = new MonitorInfo(-1920, 0, 1920, 1080, 100, false);
        IMonitors monitors = new FakeMonitors(one) { Monitors = [left, one] };

        Assert.Equal([left, one], monitors.Read());
    }

    [Fact]
    public void UnavailableMonitorsThrowWhileTheDesktopIsStillThere()
    {
        IDesktopIcons desktop = Seam(new FakeDesktopIcons(A));
        IMonitors monitors = new FakeMonitors(new MonitorInfo(0, 0, 1, 1, 100, true)) { Unavailable = true };

        Assert.Throws<DesktopUnavailableException>(() => monitors.Read());
        Assert.Equal([A], desktop.ReadIcons());
    }

    [Fact]
    public void NoMonitorsLeftIsUnavailableNotAnEmptyList()
    {
        var fake = new FakeMonitors(new MonitorInfo(0, 0, 1, 1, 100, true));
        fake.Monitors.Clear();

        Assert.Throws<DesktopUnavailableException>(() => Seam(fake).Read());
    }

    [Fact]
    public void SetPositionsWithBeforeMoveDistinguishesPrePreparationFromPlacementFailure()
    {
        var fake = new FakeDesktopIcons(A) { Unavailable = true };
        IDesktopIcons desktop = Seam(fake);
        int beforeMoveCalls = 0;

        Assert.Throws<DesktopUnavailableException>(() => desktop.SetPositions([A with { X = 99 }], () => beforeMoveCalls++));
        Assert.Equal(0, beforeMoveCalls);
        Assert.Empty(fake.Calls);

        fake.Unavailable = false;
        fake.UnavailableOnSet = true;
        Assert.Throws<DesktopUnavailableException>(() => desktop.SetPositions([A with { X = 99 }], () => beforeMoveCalls++));
        Assert.Equal(1, beforeMoveCalls);
        Assert.Single(fake.Calls);
    }
}
