using Berth.Core.Model;
using Berth.Window.Map;

namespace Berth.Tests.Window;

public sealed class MapGeometryTests
{
    [Fact]
    public void FitSingleMonitorAtOriginComputesExactLiteralCoordinates()
    {
        var mon = new MonitorInfo(0, 0, 2000, 1000, 100, true);
        // Panel 448 x 268, padding 24, labelHeight 20 -> availW = 400, availH = 200 -> exact scale = 0.2.
        // drawnW = 400, drawnH = 200 -> offsetX = 24, offsetY = 44.
        MapGeometry geo = MapGeometry.Fit([mon], panelWidth: 448, panelHeight: 268, padding: 24, labelHeight: 20);

        Assert.True(geo.HasGeometry);
        Assert.Equal(0.2d, geo.Scale, precision: 6);
        Assert.Equal(24d, geo.OffsetX, precision: 6);
        Assert.Equal(44d, geo.OffsetY, precision: 6);

        MapPoint origin = geo.ToPanel(0, 0);
        Assert.Equal(24d, origin.X, precision: 6);
        Assert.Equal(44d, origin.Y, precision: 6);

        MapPoint bottomRight = geo.ToPanel(2000, 1000);
        Assert.Equal(424d, bottomRight.X, precision: 6);
        Assert.Equal(244d, bottomRight.Y, precision: 6);

        MapRect rect = geo.MonitorRect(mon);
        Assert.Equal(24d, rect.X, precision: 6);
        Assert.Equal(44d, rect.Y, precision: 6);
        Assert.Equal(400d, rect.Width, precision: 6);
        Assert.Equal(200d, rect.Height, precision: 6);
    }

    [Fact]
    public void FitTwoSideBySideMonitorsCentersHorizontallyAndPreservesProportions()
    {
        var left = new MonitorInfo(0, 0, 2560, 1440, 100, true);
        var right = new MonitorInfo(2560, 0, 1920, 1080, 100, false);
        // boxW = 4480, boxH = 1440. Panel 548 x 300, pad 24, label 20 -> availW = 500, availH = 232.
        // scale = min(500/4480, 232/1440) = 500/4480 = 0.111607142857...
        MapGeometry geo = MapGeometry.Fit([left, right], panelWidth: 548, panelHeight: 300, padding: 24, labelHeight: 20);

        double expectedScale = 500d / 4480d;
        double expectedOffsetY = 24d + 20d + ((232d - (1440d * expectedScale)) / 2d);
        Assert.Equal(expectedScale, geo.Scale, precision: 6);
        Assert.Equal(24d, geo.OffsetX, precision: 6);
        Assert.Equal(expectedOffsetY, geo.OffsetY, precision: 6);

        MapRect r1 = geo.MonitorRect(left);
        MapRect r2 = geo.MonitorRect(right);
        Assert.Equal(r1.X + r1.Width, r2.X, precision: 6);
        Assert.Equal(2560d / 1440d, r1.Width / r1.Height, precision: 6);
        Assert.Equal(1920d / 1080d, r2.Width / r2.Height, precision: 6);
    }

    [Fact]
    public void FitNegativeXAndNegativeYMonitorMapsTopLeftInsidePaddedPanel()
    {
        var neg = new MonitorInfo(-1920, -200, 1920, 1080, 100, false);
        var main = new MonitorInfo(0, 0, 2560, 1440, 100, true);
        // minX = -1920, maxX = 2560 -> boxW = 4480; minY = -200, maxY = 1440 -> boxH = 1640.
        // Panel 496 x 268, pad 24, label 20 -> availW = 448, availH = 200 -> scale = min(448/4480=0.1, 200/1640=0.12195) = 0.1.
        // drawnW = 448, drawnH = 164 -> offsetX = 24, offsetY = 44 + (200 - 164)/2 = 62.
        MapGeometry geo = MapGeometry.Fit([neg, main], panelWidth: 496, panelHeight: 268, padding: 24, labelHeight: 20);

        Assert.Equal(0.1d, geo.Scale, precision: 6);
        Assert.Equal(24d, geo.OffsetX, precision: 6);
        Assert.Equal(62d, geo.OffsetY, precision: 6);

        // Literal expected panel coordinates independent of MonitorRect so ignoring minX/minY fails!
        MapPoint negTopLeft = geo.ToPanel(-1920, -200);
        Assert.Equal(24d, negTopLeft.X, precision: 6);
        Assert.Equal(62d, negTopLeft.Y, precision: 6);

        MapPoint mainTopLeft = geo.ToPanel(0, 0);
        Assert.Equal(216d, mainTopLeft.X, precision: 6);
        Assert.Equal(82d, mainTopLeft.Y, precision: 6);

        MapRect negRect = geo.MonitorRect(neg);
        Assert.Equal(negTopLeft.X, negRect.X, precision: 6);
        Assert.Equal(negTopLeft.Y, negRect.Y, precision: 6);
        Assert.True(negRect.X >= 24d && negRect.Y >= 44d);
    }

    [Fact]
    public void FitPortraitMonitorAndTallPanelKeepsProportionsAndCenters()
    {
        var portrait = new MonitorInfo(0, 0, 1080, 1920, 100, true);
        // Panel taller than wide: 248 x 668, pad 24, label 20 -> availW = 200, availH = 600.
        // scale = min(200/1080, 600/1920) = 200/1080 = 0.185185...
        MapGeometry geo = MapGeometry.Fit([portrait], panelWidth: 248, panelHeight: 668, padding: 24, labelHeight: 20);

        double expectedScale = 200d / 1080d;
        double drawnH = 1920d * expectedScale;
        double expectedOffsetY = 24d + 20d + ((600d - drawnH) / 2d);
        Assert.Equal(expectedScale, geo.Scale, precision: 6);
        Assert.Equal(24d, geo.OffsetX, precision: 6);
        Assert.Equal(expectedOffsetY, geo.OffsetY, precision: 6);

        MapRect rect = geo.MonitorRect(portrait);
        Assert.Equal(1080d / 1920d, rect.Width / rect.Height, precision: 6);
    }

    [Fact]
    public void ClampProjectsOffScreenPointsOntoNearestMonitorInclusiveEdge()
    {
        var left = new MonitorInfo(0, 0, 1000, 1000, 100, true);
        var right = new MonitorInfo(2000, 0, 1000, 1000, 100, false);

        // On-screen point -> unchanged, OffScreen false.
        Assert.Equal(new ClampedDesktopPoint(500, 500, false), MapGeometry.Clamp(500, 500, [left, right]));

        // Far outside left -> clamped to left's top-left (0, 0).
        Assert.Equal(new ClampedDesktopPoint(0, 0, true), MapGeometry.Clamp(-50000, -50000, [left, right]));

        // Far outside right -> clamped to right's inclusive bottom-right (2999, 999).
        Assert.Equal(new ClampedDesktopPoint(2999, 999, true), MapGeometry.Clamp(999999, 999999, [left, right]));

        // True equidistant gap: left inclusive edge is X=999, right starts at X=2001 -> X=1500 is exactly 501 px from
        // both, so MonitorNumbering.Order tie-break selects monitor 1 (left, X=999).
        var rightTie = new MonitorInfo(2001, 0, 1000, 1000, 100, false);
        Assert.Equal(new ClampedDesktopPoint(999, 500, true), MapGeometry.Clamp(1500, 500, [left, rightTie]));
        Assert.Equal(new ClampedDesktopPoint(2001, 500, true), MapGeometry.Clamp(1501, 500, [left, rightTie]));

        // Shared edge (adjacent monitors): x = 1000 belongs to the right monitor (inclusive [1000, 1999]).
        var adjRight = new MonitorInfo(1000, 0, 1000, 1000, 100, false);
        Assert.Equal(new ClampedDesktopPoint(1000, 500, false), MapGeometry.Clamp(1000, 500, [left, adjRight]));

        // Extreme coordinates (int.MinValue / int.MaxValue) do not overflow.
        Assert.Equal(new ClampedDesktopPoint(0, 0, true), MapGeometry.Clamp(int.MinValue, int.MinValue, [left, right]));
        Assert.Equal(new ClampedDesktopPoint(2999, 999, true), MapGeometry.Clamp(int.MaxValue, int.MaxValue, [left, right]));

        // Empty monitor list returns original point with OffScreen false without throwing.
        Assert.Equal(new ClampedDesktopPoint(123, 456, false), MapGeometry.Clamp(123, 456, []));
    }

    [Fact]
    public void FormatMonitorLabelFormatsNumberDimensionsAndPrimarySuffix()
    {
        var primary = new MonitorInfo(0, 0, 2560, 1440, 100, true);
        var secondary = new MonitorInfo(2560, 0, 1920, 1080, 100, false);

        Assert.Equal("1 \u00B7 2560 \u00D7 1440 \u00B7 primary", MapGeometry.FormatMonitorLabel(1, primary));
        Assert.Equal("2 \u00B7 1920 \u00D7 1080", MapGeometry.FormatMonitorLabel(2, secondary));
    }
}
