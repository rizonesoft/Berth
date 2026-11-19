using System.Globalization;
using Berth.Core.Model;

namespace Berth.Core;

/// <summary>Date, count and monitor size formats shared by the window, the tray and the CLI (SPEC §3, §13).</summary>
internal static class Formats
{
    /// <summary>
    /// Formats <paramref name="value"/> relative to <paramref name="now"/> in <paramref name="zone"/> as
    /// <c>today, HH:mm</c>, <c>yesterday, HH:mm</c>, <c>d MMMM</c> (same local year), or <c>d MMMM yyyy</c>
    /// using 24-hour time and invariant English month names (SPEC §3).
    /// </summary>
    public static string When(DateTimeOffset value, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTimeOffset localValue = TimeZoneInfo.ConvertTime(value, zone);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, zone);
        int daysAgo = DateOnly.FromDateTime(localNow.DateTime).DayNumber - DateOnly.FromDateTime(localValue.DateTime).DayNumber;

        return daysAgo switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"today, {localValue:HH:mm}"),
            1 => string.Create(CultureInfo.InvariantCulture, $"yesterday, {localValue:HH:mm}"),
            _ when localValue.Year == localNow.Year => localValue.ToString("d MMMM", CultureInfo.InvariantCulture),
            _ => localValue.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Formats <paramref name="monitors"/> in <see cref="MonitorNumbering.Order"/> as <c>2560 × 1440</c>,
    /// <c>A and B</c>, or <c>A, B and C</c> using the multiplication sign U+00D7 (SPEC §3).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="monitors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="monitors"/> is empty, contains null, or has a non-positive width or height.</exception>
    public static string MonitorSizes(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("An arrangement has at least one monitor.", nameof(monitors));
        }

        foreach (MonitorInfo? monitor in monitors)
        {
            if (monitor is null)
            {
                throw new ArgumentException("Monitor list must not contain null entries.", nameof(monitors));
            }

            if (monitor.Width <= 0 || monitor.Height <= 0)
            {
                throw new ArgumentException("Every monitor must have a positive width and height.", nameof(monitors));
            }
        }

        string[] parts = [.. MonitorNumbering.Order(monitors).Select(Size)];
        return parts.Length switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    /// <summary>Formats a monitor count: <c>1 monitor</c> or <c>2 monitors</c> (SPEC §5, §13).</summary>
    public static string MonitorCount(int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        return n == 1 ? "1 monitor" : string.Create(CultureInfo.InvariantCulture, $"{n} monitors");
    }

    /// <summary>Formats an icon count: <c>1 icon</c> or <c>42 icons</c> (SPEC §5, §13).</summary>
    public static string IconCount(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        return n == 1 ? "1 icon" : string.Create(CultureInfo.InvariantCulture, $"{n} icons");
    }

    /// <summary>Formats <paramref name="value"/> in <paramref name="zone"/> as <c>yyyy-MM-dd HH:mm</c> for <c>berth list</c> (SPEC §13).</summary>
    public static string ListDate(DateTimeOffset value, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Size(MonitorInfo monitor) =>
        string.Create(CultureInfo.InvariantCulture, $"{monitor.Width} \u00D7 {monitor.Height}");
}
