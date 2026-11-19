using System.Globalization;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class FormatsTests
{
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("PlusTwo", TimeSpan.FromHours(2), "UTC+2", "UTC+2");
    private static readonly TimeZoneInfo MinusFive = TimeZoneInfo.CreateCustomTimeZone("MinusFive", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    [Fact]
    public void WhenUsesLocalDatesAcrossMidnightInNonUtcZones()
    {
        // In UTC both instants share 2026-11-15, while in UTC-5 value is on the local evening before.
        DateTimeOffset minusFiveValue = new(2026, 11, 15, 4, 2, 0, TimeSpan.Zero);
        DateTimeOffset minusFiveNow = new(2026, 11, 15, 5, 30, 0, TimeSpan.Zero);
        Assert.Equal("yesterday, 23:02", Formats.When(minusFiveValue, minusFiveNow, MinusFive));

        // In UTC value is 2026-11-14 (yesterday), while in UTC+2 both instants fall on 2026-11-15.
        DateTimeOffset plusTwoValue = new(2026, 11, 14, 22, 14, 0, TimeSpan.Zero);
        DateTimeOffset plusTwoNow = new(2026, 11, 15, 7, 30, 0, TimeSpan.Zero);
        Assert.Equal("today, 00:14", Formats.When(plusTwoValue, plusTwoNow, PlusTwo));

        // SPEC §3 and §14 reference timestamp (07:14Z -> 09:14 in UTC+2, and 15:02Z day before -> yesterday, 17:02).
        DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal("today, 09:14", Formats.When(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), now, PlusTwo));
        Assert.Equal("yesterday, 17:02", Formats.When(new DateTimeOffset(2026, 11, 14, 15, 2, 0, TimeSpan.Zero), now, PlusTwo));
    }

    [Fact]
    public void WhenRespectsLocalMidnightBoundaryBetweenYesterdayAndEarlierDates()
    {
        DateTimeOffset now = new(2026, 11, 15, 0, 1, 0, TimeSpan.FromHours(2));

        Assert.Equal("13 November", Formats.When(new DateTimeOffset(2026, 11, 13, 23, 59, 59, TimeSpan.FromHours(2)), now, PlusTwo));
        Assert.Equal("yesterday, 00:00", Formats.When(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.FromHours(2)), now, PlusTwo));
        Assert.Equal("yesterday, 23:59", Formats.When(new DateTimeOffset(2026, 11, 14, 23, 59, 59, TimeSpan.FromHours(2)), now, PlusTwo));
        Assert.Equal("today, 00:00", Formats.When(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromHours(2)), now, PlusTwo));
    }

    [Fact]
    public void WhenGivesYesterdayPrecedenceAcrossNewYearAndAddsYearOtherwise()
    {
        DateTimeOffset newYearsDay = new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("yesterday, 23:59", Formats.When(new DateTimeOffset(2025, 12, 31, 23, 59, 0, TimeSpan.FromHours(2)), newYearsDay, PlusTwo));
        Assert.Equal("30 December 2025", Formats.When(new DateTimeOffset(2025, 12, 30, 14, 5, 0, TimeSpan.FromHours(2)), newYearsDay, PlusTwo));
        Assert.Equal("today, 01:00", Formats.When(new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 21, 59, 0, TimeSpan.Zero), PlusTwo));

        // Single-digit days have no leading zero, and another year appends yyyy.
        Assert.Equal("5 March", Formats.When(new DateTimeOffset(2026, 3, 5, 9, 5, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(2)), PlusTwo));
        Assert.Equal("5 March 2025", Formats.When(new DateTimeOffset(2025, 3, 5, 9, 5, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(2)), PlusTwo));
    }

    [Fact]
    public void WhenHandlesDaylightSavingTransitionsFutureDatesMixedOffsetsAndNullZone()
    {
        TimeZoneInfo dstZone = CustomDstZone();

        // Spring-forward day has 23 hours: 23 elapsed hours across local midnight is still yesterday.
        DateTimeOffset afterSpringMidnight = new(2026, 3, 9, 0, 30, 0, TimeSpan.FromHours(-4));
        DateTimeOffset springYesterday = new(2026, 3, 8, 0, 30, 0, TimeSpan.FromHours(-5));
        Assert.Equal(TimeSpan.FromHours(23), afterSpringMidnight - springYesterday);
        Assert.Equal("yesterday, 00:30", Formats.When(springYesterday, afterSpringMidnight, dstZone));

        // Fall-back day has 25 hours: 24 elapsed hours inside the same local date is still today,
        // and 25 hours across the prior midnight is yesterday.
        DateTimeOffset fallMorning = new(2026, 11, 1, 0, 30, 0, TimeSpan.FromHours(-4));
        DateTimeOffset fallLateNight = new(2026, 11, 1, 23, 30, 0, TimeSpan.FromHours(-5));
        Assert.Equal(TimeSpan.FromHours(24), fallLateNight - fallMorning);
        Assert.Equal("today, 00:30", Formats.When(fallMorning, fallLateNight, dstZone));

        DateTimeOffset dayAfterFall = new(2026, 11, 2, 1, 30, 0, TimeSpan.FromHours(-5));
        DateTimeOffset dayOfFall = new(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4));
        Assert.Equal(TimeSpan.FromHours(25), dayAfterFall - dayOfFall);
        Assert.Equal("yesterday, 01:30", Formats.When(dayOfFall, dayAfterFall, dstZone));

        // Future values use today on the same local date, else date without or with year (never yesterday).
        DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal("today, 18:45", Formats.When(new DateTimeOffset(2026, 11, 15, 18, 45, 0, TimeSpan.FromHours(2)), now, PlusTwo));
        Assert.Equal("16 November", Formats.When(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(2)), now, PlusTwo));
        Assert.Equal("2 January 2027", Formats.When(new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.FromHours(2)), now, PlusTwo));

        // Mixed non-UTC input offsets convert cleanly into the target zone.
        Assert.Equal(
            "yesterday, 23:30",
            Formats.When(
                new DateTimeOffset(2026, 11, 15, 6, 30, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 11, 15, 1, 15, 0, TimeSpan.FromHours(-8)),
                PlusTwo));

        Assert.Throws<ArgumentNullException>(() => Formats.When(now, now, null!));
    }

    [Fact]
    public void MonitorSizesOrdersAndJoinsWithMultiplicationSign()
    {
        var left = new MonitorInfo(-1920, 200, 1920, 1080, 100, false);
        var main = new MonitorInfo(0, 0, 2560, 1440, 100, true);
        var lower = new MonitorInfo(0, 1440, 1600, 900, 100, false);
        var far = new MonitorInfo(2560, 0, 3840, 2160, 150, false);

        string one = Formats.MonitorSizes([main]);
        Assert.Equal("2560 \u00D7 1440", one);
        Assert.Contains('\u00D7', one);
        Assert.DoesNotContain('x', one);

        Assert.Equal("1920 \u00D7 1080 and 2560 \u00D7 1440", Formats.MonitorSizes([main, left]));
        Assert.Equal("1920 \u00D7 1080, 2560 \u00D7 1440 and 1600 \u00D7 900", Formats.MonitorSizes([lower, main, left]));
        Assert.Equal("1920 \u00D7 1080, 2560 \u00D7 1440, 1600 \u00D7 900 and 3840 \u00D7 2160", Formats.MonitorSizes([far, lower, left, main]));

        Assert.Throws<ArgumentNullException>(() => Formats.MonitorSizes(null!));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([]));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([null!]));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([main, null!]));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([main with { Width = 0 }]));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([main with { Width = -1 }]));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([main with { Height = 0 }]));
        Assert.Throws<ArgumentException>(() => Formats.MonitorSizes([main with { Height = -10 }]));
    }

    [Fact]
    public void CountsFormatSingularAndPluralAndRejectOutOfRange()
    {
        Assert.Equal("1 monitor", Formats.MonitorCount(1));
        Assert.Equal("2 monitors", Formats.MonitorCount(2));
        Assert.Equal("3 monitors", Formats.MonitorCount(3));
        Assert.Equal("1000 monitors", Formats.MonitorCount(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => Formats.MonitorCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Formats.MonitorCount(-1));

        Assert.Equal("0 icons", Formats.IconCount(0));
        Assert.Equal("1 icon", Formats.IconCount(1));
        Assert.Equal("2 icons", Formats.IconCount(2));
        Assert.Equal("42 icons", Formats.IconCount(42));
        Assert.Equal("1000 icons", Formats.IconCount(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => Formats.IconCount(-1));
    }

    [Fact]
    public void ListDateConvertsToZoneAndTruncatesSeconds()
    {
        Assert.Equal("2026-11-15 09:14", Formats.ListDate(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), PlusTwo));
        Assert.Equal("2026-01-01 01:30", Formats.ListDate(new DateTimeOffset(2025, 12, 31, 23, 30, 59, TimeSpan.Zero), PlusTwo));
        Assert.Equal("2026-11-15 19:05", Formats.ListDate(new DateTimeOffset(2026, 11, 15, 12, 5, 30, TimeSpan.FromHours(-5)), PlusTwo));
        Assert.Throws<ArgumentNullException>(() => Formats.ListDate(DateTimeOffset.UtcNow, null!));
    }

    [Fact]
    public void AllFormatsUseInvariantEnglishRegardlessOfCurrentCulture()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        var hostile = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        hostile.DateTimeFormat.TimeSeparator = ".";
        hostile.DateTimeFormat.DateSeparator = "/";
        hostile.NumberFormat.NumberGroupSeparator = " ";

        try
        {
            CultureInfo.CurrentCulture = hostile;
            CultureInfo.CurrentUICulture = hostile;

            DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
            Assert.Equal("today, 09:14", Formats.When(new DateTimeOffset(2026, 11, 15, 7, 14, 0, TimeSpan.Zero), now, PlusTwo));
            Assert.Equal("yesterday, 17:02", Formats.When(new DateTimeOffset(2026, 11, 14, 15, 2, 0, TimeSpan.Zero), now, PlusTwo));
            Assert.Equal("12 November", Formats.When(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(2)), now, PlusTwo));
            Assert.Equal("5 March 2025", Formats.When(new DateTimeOffset(2025, 3, 5, 9, 0, 0, TimeSpan.FromHours(2)), now, PlusTwo));
            Assert.Equal("2560 \u00D7 1440", Formats.MonitorSizes([new MonitorInfo(0, 0, 2560, 1440, 100, true)]));
            Assert.Equal("1000 monitors", Formats.MonitorCount(1000));
            Assert.Equal("1000 icons", Formats.IconCount(1000));
            Assert.Equal("2026-11-15 09:14", Formats.ListDate(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), PlusTwo));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static TimeZoneInfo CustomDstZone()
    {
        var spring = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var fall = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020, 1, 1), new DateTime(2030, 12, 31), TimeSpan.FromHours(1), spring, fall);
        return TimeZoneInfo.CreateCustomTimeZone("CustomDst", TimeSpan.FromHours(-5), "Custom DST", "Custom ST", "Custom DT", [rule]);
    }
}
