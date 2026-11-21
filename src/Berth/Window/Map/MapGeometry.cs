using System.Globalization;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Window.Map;

/// <summary>
/// Pure geometry mapping between virtual-desktop physical pixels and the position map's panel coordinates (SPEC §7).
/// Performs no coordinate conversion beyond uniform scaling and centering into the padded viewport.
/// </summary>
internal readonly record struct MapRect(double X, double Y, double Width, double Height);

internal readonly record struct MapPoint(double X, double Y);

internal readonly record struct ClampedDesktopPoint(int X, int Y, bool OffScreen);

internal sealed class MapGeometry
{
    public static readonly MapGeometry Empty = new(0, 0, 0d, 0d, 0d, hasGeometry: false);

    private readonly long _minX;
    private readonly long _minY;

    private MapGeometry(long minX, long minY, double scale, double offsetX, double offsetY, bool hasGeometry)
    {
        _minX = minX;
        _minY = minY;
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
        HasGeometry = hasGeometry;
    }

    public bool HasGeometry { get; }

    public double Scale { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    /// <summary>
    /// Computes the uniform scale and centered offset that fit <paramref name="monitors"/> inside
    /// <paramref name="panelWidth"/> by <paramref name="panelHeight"/> with <paramref name="padding"/> on every side
    /// and <paramref name="labelHeight"/> reserved above the top edge for monitor labels.
    /// </summary>
    public static MapGeometry Fit(
        IReadOnlyList<MonitorInfo> monitors,
        double panelWidth,
        double panelHeight,
        double padding,
        double labelHeight)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0
            || !double.IsFinite(panelWidth)
            || !double.IsFinite(panelHeight)
            || !double.IsFinite(padding)
            || !double.IsFinite(labelHeight)
            || panelWidth <= 0d
            || panelHeight <= 0d)
        {
            return Empty;
        }

        double availW = panelWidth - (2d * padding);
        double availH = panelHeight - (2d * padding) - labelHeight;
        if (availW <= 0d || availH <= 0d)
        {
            return Empty;
        }

        long minX = long.MaxValue;
        long minY = long.MaxValue;
        long maxX = long.MinValue;
        long maxY = long.MinValue;

        foreach (MonitorInfo m in monitors)
        {
            if (m.Width <= 0 || m.Height <= 0)
            {
                return Empty;
            }

            long left = m.X;
            long top = m.Y;
            long right = (long)m.X + m.Width;
            long bottom = (long)m.Y + m.Height;

            if (left < minX) minX = left;
            if (top < minY) minY = top;
            if (right > maxX) maxX = right;
            if (bottom > maxY) maxY = bottom;
        }

        double boxW = (double)(maxX - minX);
        double boxH = (double)(maxY - minY);
        if (boxW <= 0d || boxH <= 0d)
        {
            return Empty;
        }

        double scale = Math.Min(availW / boxW, availH / boxH);
        double drawnW = boxW * scale;
        double drawnH = boxH * scale;
        double offsetX = padding + ((availW - drawnW) / 2d);
        double offsetY = padding + labelHeight + ((availH - drawnH) / 2d);

        return new MapGeometry(minX, minY, scale, offsetX, offsetY, hasGeometry: true);
    }

    /// <summary>Maps a virtual-desktop physical pixel coordinate directly into panel coordinates.</summary>
    public MapPoint ToPanel(int x, int y)
    {
        if (!HasGeometry)
        {
            return new MapPoint(0d, 0d);
        }

        double px = OffsetX + (((long)x - _minX) * Scale);
        double py = OffsetY + (((long)y - _minY) * Scale);
        return new MapPoint(px, py);
    }

    /// <summary>Returns the panel rectangle for <paramref name="monitor"/>.</summary>
    public MapRect MonitorRect(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (!HasGeometry)
        {
            return new MapRect(0d, 0d, 0d, 0d);
        }

        MapPoint topLeft = ToPanel(monitor.X, monitor.Y);
        return new MapRect(topLeft.X, topLeft.Y, monitor.Width * Scale, monitor.Height * Scale);
    }

    /// <summary>
    /// Places an off-screen icon at the nearest point on the nearest monitor's inclusive edge (SPEC §7, last bullet),
    /// matching <see cref="MonitorNumbering.Locate"/>. Returns (<paramref name="x"/>, <paramref name="y"/>, false) when
    /// <paramref name="monitors"/> is empty or the point already lies on a monitor.
    /// </summary>
    public static ClampedDesktopPoint Clamp(int x, int y, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return new ClampedDesktopPoint(x, y, OffScreen: false);
        }

        return ClampOrdered(x, y, MonitorNumbering.Order(monitors));
    }

    /// <summary>
    /// Overload of <see cref="Clamp"/> for callers that already hold <paramref name="ordered"/> in
    /// <see cref="MonitorNumbering.Order"/>, avoiding repeated sorts per icon.
    /// </summary>
    public static ClampedDesktopPoint ClampOrdered(int x, int y, IReadOnlyList<MonitorInfo> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        if (ordered.Count == 0)
        {
            return new ClampedDesktopPoint(x, y, OffScreen: false);
        }

        (int number, bool offScreen) = MonitorNumbering.Locate(ordered, x, y);
        if (!offScreen)
        {
            return new ClampedDesktopPoint(x, y, OffScreen: false);
        }

        MonitorInfo m = ordered[number - 1];
        long minX = m.X;
        long maxX = (long)m.X + m.Width - 1;
        long minY = m.Y;
        long maxY = (long)m.Y + m.Height - 1;

        long clampedX = Math.Clamp((long)x, minX, maxX);
        long clampedY = Math.Clamp((long)y, minY, maxY);
        return new ClampedDesktopPoint(
            (int)Math.Clamp(clampedX, int.MinValue, int.MaxValue),
            (int)Math.Clamp(clampedY, int.MinValue, int.MaxValue),
            OffScreen: true);
    }

    /// <summary>Formats a monitor label as <c>1 · 2560 × 1440</c>, ending with <c> · primary</c> on the primary.</summary>
    public static string FormatMonitorLabel(int number, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        string baseLabel = string.Create(
            CultureInfo.InvariantCulture,
            $"{number} \u00B7 {monitor.Width} \u00D7 {monitor.Height}");
        return monitor.Primary ? $"{baseLabel} \u00B7 primary" : baseLabel;
    }
}
