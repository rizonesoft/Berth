using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class LayoutComparisonTests
{
    private static readonly MonitorInfo Main = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo Left = new(-1920, 0, 1920, 1080, 100, false);

    private static Layout With(params IconPosition[] icons) => Layouts.Make("Docked", [Left, Main], icons);

    private static IconPosition Icon(string name, int x, int y) => new(@"C:\Desk\" + name, name, x, y);

    [Fact]
    public void EachSavedIconIsInPlaceMovedOrMissing()
    {
        IconPosition a = Icon("a.txt", 10, 10), b = Icon("b.txt", 20, 20), c = Icon("c.txt", 30, 30), d = Icon("d.txt", 40, 40), e = Icon("e.txt", 50, 50), f = Icon("f.txt", 60, 60);
        IconPosition bNow = b with { X = 21 }, cNow = c with { Y = 31 };

        LayoutComparison result = LayoutComparison.Compare(With(a, b, c, d, e, f), [a, bNow, cNow, e, f]);

        Assert.Equal(
            [
                new ComparedIcon(a, IconState.InPlace, a, 2, false),
                new ComparedIcon(b, IconState.Moved, bNow, 2, false),
                new ComparedIcon(c, IconState.Moved, cNow, 2, false),
                new ComparedIcon(d, IconState.Missing, null, 2, false),
                new ComparedIcon(e, IconState.InPlace, e, 2, false),
                new ComparedIcon(f, IconState.InPlace, f, 2, false),
            ],
            result.Icons);
        Assert.Equal((3, 2, 1), (result.InPlace, result.Moved, result.Missing));
    }

    [Fact]
    public void IconsMatchByParsingNameIgnoringCaseNeverByDisplayName()
    {
        IconPosition saved = new(@"C:\Desk\notes.txt", "Notes", 100, 100);
        IconPosition sameIdNewName = new(@"c:\DESK\NOTES.TXT", "Renamed", 100, 100);
        IconPosition otherWithSavedName = new(@"C:\Desk\other.txt", "Notes", 500, 500);

        ComparedIcon only = Assert.Single(LayoutComparison.Compare(With(saved), [otherWithSavedName, sameIdNewName]).Icons);

        Assert.Equal(IconState.InPlace, only.State);
        Assert.Equal(@"c:\DESK\NOTES.TXT", only.Current!.Id);

        // Another icon with the saved display name is never taken for it.
        ComparedIcon alone = Assert.Single(LayoutComparison.Compare(With(saved), [otherWithSavedName]).Icons);
        Assert.Equal(IconState.Missing, alone.State);
        Assert.Null(alone.Current);
    }

    [Fact]
    public void NoCoordinateOrSizeWrapsTheMonitorArithmetic()
    {
        // A monitor whose right edge passes int.MaxValue still holds its own origin.
        var far = new MonitorInfo(2147483640, 0, 1920, 1080, 100, true);
        Assert.Equal((1, false), MonitorNumbering.Locate([far], 2147483640, 0));

        // The nearer monitor wins even when a squared distance passes long.MaxValue.
        Assert.Equal((1, true), MonitorNumbering.Locate(MonitorNumbering.Order([Main, Left]), int.MinValue, int.MinValue));
    }

    [Fact]
    public void IconsOnlyOnTheDesktopAreLeftOutAndAnEmptyDesktopMissesEverySavedIcon()
    {
        IconPosition a = Icon("a.txt", 10, 10), b = Icon("b.txt", 20, 20);

        Assert.Equal([a], LayoutComparison.Compare(With(a), [a, b]).Icons.Select(i => i.Saved));

        LayoutComparison empty = LayoutComparison.Compare(With(a, b), []);
        Assert.All(empty.Icons, i => Assert.Equal(IconState.Missing, i.State));
        Assert.Equal((0, 0, 2), (empty.InPlace, empty.Moved, empty.Missing));
    }

    [Fact]
    public void ANeutralLayoutPlotsItsSavedPositions()
    {
        IconPosition a = Icon("a.txt", 10, 10), b = Icon("b.txt", -1800, 40);

        LayoutComparison result = LayoutComparison.Neutral(With(a, b));

        Assert.Equal([new ComparedIcon(a, IconState.Neutral, null, 2, false), new ComparedIcon(b, IconState.Neutral, null, 1, false)], result.Icons);
        Assert.Equal((0, 0, 0), (result.InPlace, result.Moved, result.Missing));
    }

    [Fact]
    public void TheUnsavedArrangementPlotsTheLivePositions()
    {
        IconPosition a = Icon("a.txt", 10, 10), b = Icon("b.txt", -1800, 40);

        LayoutComparison result = LayoutComparison.Unsaved([Main, Left], [a, b]);

        Assert.Equal([new ComparedIcon(a, IconState.Neutral, null, 2, false), new ComparedIcon(b, IconState.Neutral, null, 1, false)], result.Icons);
        Assert.Equal((0, 0, 0), (result.InPlace, result.Moved, result.Missing));
    }

    [Fact]
    public void MonitorsAreNumberedLeftToRightThenTopToBottom()
    {
        // The upper right monitor sits higher than the others, so top-to-bottom-first numbering would differ.
        var upper = new MonitorInfo(2560, -500, 1920, 1080, 100, false);
        var lower = new MonitorInfo(2560, 580, 1920, 1080, 100, false);

        Assert.Equal([Left, Main, upper, lower], MonitorNumbering.Order([lower, Main, upper, Left]));
    }

    [Fact]
    public void TheSavedPositionDecidesTheMonitorAndASharedEdgeBelongsToTheRight()
    {
        IconPosition onEdge = Icon("edge.txt", 0, 100);
        IconPosition lastLeftPixel = Icon("left.txt", -1, 100);

        LayoutComparison result = LayoutComparison.Compare(With(onEdge, lastLeftPixel), [onEdge with { X = -500 }, lastLeftPixel]);

        ComparedIcon edge = result.Icons.Single(i => i.Saved == onEdge);
        Assert.Equal((2, false), (edge.MonitorNumber, edge.OffScreen));
        Assert.Equal(IconState.Moved, edge.State);
        ComparedIcon left = result.Icons.Single(i => i.Saved == lastLeftPixel);
        Assert.Equal((1, false), (left.MonitorNumber, left.OffScreen));
    }

    [Fact]
    public void AnIconOutsideEveryMonitorGetsTheNearestByDistanceToItsRectangle()
    {
        // Below the small monitor (number 1, whose centre is nearest) but nearer the big one's edge (number 2).
        var small = new MonitorInfo(0, 0, 1000, 1000, 100, true);
        var big = new MonitorInfo(1000, 0, 3000, 3000, 100, false);
        Layout layout = Layouts.Make("Wide", [big, small], [Icon("far.txt", 600, 1500), Icon("off.txt", 5000, 5000)]);

        ComparedIcon[] icons = [.. LayoutComparison.Neutral(layout).Icons];

        Assert.Equal((2, true), (icons.Single(i => i.Saved.Name == "far.txt").MonitorNumber, icons.Single(i => i.Saved.Name == "far.txt").OffScreen));
        ComparedIcon off = icons.Single(i => i.Saved.Name == "off.txt");
        Assert.Equal((2, true), (off.MonitorNumber, off.OffScreen));
    }

    [Fact]
    public void IconsComeInReadingOrderOfTheirSavedPositions()
    {
        IconPosition right = Icon("right.txt", 900, 100), leftSameRow = Icon("left.txt", 100, 100), top = Icon("top.txt", 500, 10), bottom = Icon("bottom.txt", 50, 700);

        // The top icon has moved below everything; the order still follows where it was saved.
        LayoutComparison result = LayoutComparison.Compare(With(bottom, right, top, leftSameRow), [bottom, right, top with { Y = 1300 }, leftSameRow]);

        Assert.Equal(["top.txt", "left.txt", "right.txt", "bottom.txt"], result.Icons.Select(i => i.Saved.Name));
    }
}
