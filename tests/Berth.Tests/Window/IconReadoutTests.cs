using Berth.Core;
using Berth.Core.Model;
using Berth.Window;
using Berth.Window.Map;

namespace Berth.Tests.Window;

public sealed class IconReadoutTests
{
    [Fact]
    public void FormatsExactReadoutForEveryStateOffScreenNegativeCoordsAndNoneSelected()
    {
        ReadoutLines none = IconReadout.For(null);
        Assert.False(none.HasSelection);
        Assert.Equal("Select an icon to see its position.", none.Primary);
        Assert.Equal(string.Empty, none.Secondary);

        var inPlace = new ComparedIcon(new IconPosition("id-1", "Notes.txt", 1436, 702), IconState.InPlace, new IconPosition("id-1", "Notes.txt", 1436, 702), 1, false);
        ReadoutLines rInPlace = IconReadout.For(inPlace);
        Assert.True(rInPlace.HasSelection);
        Assert.Equal("Notes.txt", rInPlace.Primary);
        Assert.Equal("x 1436 \u00B7 y 702 \u00B7 monitor 1 \u00B7 in place", rInPlace.Secondary);

        var moved = new ComparedIcon(new IconPosition("id-2", "Report.docx", 320, 480), IconState.Moved, new IconPosition("id-2", "Report.docx", 800, 600), 2, false);
        ReadoutLines rMoved = IconReadout.For(moved);
        Assert.Equal("Report.docx", rMoved.Primary);
        Assert.Equal("x 320 \u00B7 y 480 \u00B7 monitor 2 \u00B7 moved", rMoved.Secondary);

        var missing = new ComparedIcon(new IconPosition("id-3", "Old.lnk", 76, 2), IconState.Missing, null, 1, false);
        ReadoutLines rMissing = IconReadout.For(missing);
        Assert.Equal("Old.lnk", rMissing.Primary);
        Assert.Equal("x 76 \u00B7 y 2 \u00B7 monitor 1 \u00B7 not on the desktop", rMissing.Secondary);

        var neutral = new ComparedIcon(new IconPosition("id-4", "Docs", 1436, 702), IconState.Neutral, null, 1, false);
        ReadoutLines rNeutral = IconReadout.For(neutral);
        Assert.Equal("Docs", rNeutral.Primary);
        Assert.Equal("x 1436 \u00B7 y 702 \u00B7 monitor 1", rNeutral.Secondary);

        var offScreenMoved = new ComparedIcon(new IconPosition("id-5", "Stray.txt", 5000, 2000), IconState.Moved, new IconPosition("id-5", "Stray.txt", 6000, 2000), 2, true);
        Assert.Equal("x 5000 \u00B7 y 2000 \u00B7 monitor 2 \u00B7 moved \u00B7 off screen", IconReadout.For(offScreenMoved).Secondary);

        var offScreenNeutral = new ComparedIcon(new IconPosition("id-6", "Far.txt", -400, -120), IconState.Neutral, null, 1, true);
        Assert.Equal("x -400 \u00B7 y -120 \u00B7 monitor 1 \u00B7 off screen", IconReadout.For(offScreenNeutral).Secondary);
    }

    [Fact]
    public void FormatsCountForComparedPartialNeutralUnsavedAndNull()
    {
        Assert.Equal(string.Empty, IconReadout.CountText(null, ComparisonKind.None));

        ComparedIcon[] three =
        [
            new(new IconPosition("a", "A", 10, 10), IconState.InPlace, new IconPosition("a", "A", 10, 10), 1, false),
            new(new IconPosition("b", "B", 20, 20), IconState.Moved, new IconPosition("b", "B", 90, 90), 1, false),
            new(new IconPosition("c", "C", 30, 30), IconState.Missing, null, 1, false),
        ];
        var partial = new LayoutComparison(three, InPlace: 1, Moved: 1, Missing: 1);
        Assert.Equal("1 of 3 in place", IconReadout.CountText(partial, ComparisonKind.Compared));
        Assert.Equal("3 icons", IconReadout.CountText(partial, ComparisonKind.Neutral));
        Assert.Equal("3 icons", IconReadout.CountText(partial, ComparisonKind.Unsaved));

        ComparedIcon[] all42 = [.. Enumerable.Range(0, 42).Select(i =>
            new ComparedIcon(new IconPosition($"id-{i}", $"F{i}", i * 10, 10), IconState.InPlace, new IconPosition($"id-{i}", $"F{i}", i * 10, 10), 1, false))];
        var full = new LayoutComparison(all42, InPlace: 42, Moved: 0, Missing: 0);
        Assert.Equal("42 of 42 in place", IconReadout.CountText(full, ComparisonKind.Compared));
        Assert.Equal("42 icons", IconReadout.CountText(full, ComparisonKind.Neutral));

        var single = new LayoutComparison([three[0]], InPlace: 1, Moved: 0, Missing: 0);
        Assert.Equal("1 icon", IconReadout.CountText(single, ComparisonKind.Neutral));
    }

    [Fact]
    public void FormatsAutomationNameMatchingSpec7()
    {
        var icon = new ComparedIcon(new IconPosition("id-1", "Notes.txt", 1436, 702), IconState.InPlace, new IconPosition("id-1", "Notes.txt", 1436, 702), 1, false);
        Assert.Equal("Notes.txt, x 1436, y 702, monitor 1, in place", IconReadout.AutomationName(icon));

        var offScreen = new ComparedIcon(new IconPosition("id-2", "Far.txt", 9000, 100), IconState.Neutral, null, 2, true);
        Assert.Equal("Far.txt, x 9000, y 100, monitor 2, off screen", IconReadout.AutomationName(offScreen));
    }
}
