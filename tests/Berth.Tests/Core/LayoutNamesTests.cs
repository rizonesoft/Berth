using Berth.Core;

namespace Berth.Tests.Core;

public sealed class LayoutNamesTests
{
    [Theory]
    [InlineData(1, "1 monitor")]
    [InlineData(2, "2 monitors")]
    [InlineData(3, "3 monitors")]
    public void TheDefaultNameCountsTheMonitors(int monitors, string expected)
    {
        Assert.Equal(expected, LayoutNames.Default(monitors, []));
    }

    [Fact]
    public void ATakenDefaultNameGetsTheFirstFreeNumberIgnoringCase()
    {
        Assert.Equal("2 monitors (2)", LayoutNames.Default(2, ["2 MONITORS", "2 Monitors (3)"]));
        Assert.Equal("2 monitors (4)", LayoutNames.Default(2, ["2 MONITORS", "2 monitors (2)", "2 Monitors (3)"]));
    }

    [Fact]
    public void ABlankNameIsEmpty()
    {
        Assert.Equal(NameError.Empty, LayoutNames.Validate("", []));
        Assert.Equal(NameError.Empty, LayoutNames.Validate("   \t ", []));
    }

    [Fact]
    public void FortyCharactersAreAllowedAndFortyOneAreNot()
    {
        Assert.Equal(NameError.None, LayoutNames.Validate(new string('a', 40), []));
        Assert.Equal(NameError.TooLong, LayoutNames.Validate(new string('a', 41), []));
        Assert.Equal(NameError.None, LayoutNames.Validate("  " + new string('a', 40) + "   ", []));
    }

    [Fact]
    public void LengthCountsAsTheStoreCounts()
    {
        // An emoji is two UTF-16 units; the store's own rule counts string.Length, so this must agree with it.
        string forty = "\U0001F600" + new string('a', 38);
        Assert.Equal(40, forty.Length);
        Assert.Equal(NameError.None, LayoutNames.Validate(forty, []));
        Assert.Equal(NameError.TooLong, LayoutNames.Validate(forty + "a", []));
    }

    [Fact]
    public void ANameUsedByAnotherLayoutIsTakenIgnoringCaseAfterTrimming()
    {
        Assert.Equal(NameError.Taken, LayoutNames.Validate("docked", ["Docked"]));
        Assert.Equal(NameError.Taken, LayoutNames.Validate(" Docked ", ["DOCKED"]));
        Assert.Equal(NameError.None, LayoutNames.Validate(" Docked ", ["Desk"]));
        Assert.Equal(NameError.Taken, LayoutNames.Validate("Docked", ["  DOCKED "]));
    }

    [Fact]
    public void TrimmedIsTheStoredName()
    {
        Assert.Equal("Docked", LayoutNames.Trimmed("  Docked "));
    }
}
