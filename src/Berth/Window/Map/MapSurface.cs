using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Berth.Core;
using Berth.Core.Model;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfTypeface = System.Windows.Media.Typeface;

namespace Berth.Window.Map;

internal readonly record struct RenderedMonitorLabel(int Number, string Text, Rect Bounds);

/// <summary>
/// Custom drawing layer behind the marker <see cref="System.Windows.Controls.ListBox"/> (SPEC §7): renders the faint
/// grid, each monitor rectangle to scale, and collision-free monitor labels from theme tokens. Brushes are exposed as
/// <see cref="DependencyProperty"/>s with <see cref="FrameworkPropertyMetadataOptions.AffectsRender"/> so live Light,
/// Dark, and High Contrast theme changes repaint automatically without manual invalidation.
/// </summary>
internal sealed class MapSurface : FrameworkElement
{
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(WpfBrush),
        typeof(MapSurface),
        new FrameworkPropertyMetadata(WpfBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MonitorFillProperty = DependencyProperty.Register(
        nameof(MonitorFill),
        typeof(WpfBrush),
        typeof(MapSurface),
        new FrameworkPropertyMetadata(WpfBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MonitorStrokeProperty = DependencyProperty.Register(
        nameof(MonitorStroke),
        typeof(WpfBrush),
        typeof(MapSurface),
        new FrameworkPropertyMetadata(WpfBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush),
        typeof(WpfBrush),
        typeof(MapSurface),
        new FrameworkPropertyMetadata(WpfBrushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private IReadOnlyList<MonitorInfo> _orderedMonitors = [];
    private MapGeometry _geometry = MapGeometry.Empty;
    private double _padding = 24d;
    private readonly List<RenderedMonitorLabel> _renderedLabels = [];
    private readonly List<Rect> _renderedMonitorRects = [];

    public WpfBrush GridBrush
    {
        get => (WpfBrush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public WpfBrush MonitorFill
    {
        get => (WpfBrush)GetValue(MonitorFillProperty);
        set => SetValue(MonitorFillProperty, value);
    }

    public WpfBrush MonitorStroke
    {
        get => (WpfBrush)GetValue(MonitorStrokeProperty);
        set => SetValue(MonitorStrokeProperty, value);
    }

    public WpfBrush LabelBrush
    {
        get => (WpfBrush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IReadOnlyList<RenderedMonitorLabel> RenderedLabels => _renderedLabels;

    public IReadOnlyList<Rect> RenderedMonitorRects => _renderedMonitorRects;

    public void Update(IReadOnlyList<MonitorInfo> orderedMonitors, MapGeometry geometry, double padding)
    {
        _orderedMonitors = orderedMonitors;
        _geometry = geometry;
        _padding = padding;
        InvalidateVisual();
    }

    /// <summary>Measures the vertical reserve needed for one line of mono monitor label plus token gap.</summary>
    public double MeasureLabelReserve()
    {
        WpfFontFamily monoFamily = ResolveFontFamily("Berth.Type.FamilyMono", new WpfFontFamily("Consolas"));
        double monoFontSize = ResolveStyleFontSize("Berth.Type.Mono", 12d);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double gap = ResolveLabelGap();
        var sample = new FormattedText(
            "1 \u00B7 2560 \u00D7 1440 \u00B7 primary",
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new WpfTypeface(monoFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            monoFontSize,
            LabelBrush ?? WpfBrushes.Gray,
            pixelsPerDip);
        return Math.Ceiling(sample.Height + gap);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        _renderedLabels.Clear();
        _renderedMonitorRects.Clear();

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double halfPixel = 0.5d / pixelsPerDip;
        double gridStep = ResolveDouble("Berth.MapGridStep", 20d);
        double gridStroke = ResolveThicknessLeft("Berth.Space.BorderThin", 1d);
        WpfBrush gridBrush = GridBrush ?? ResolveBrush("Berth.MapGrid", WpfBrushes.Transparent);
        if (gridStep > 0d)
        {
            var gridPen = new WpfPen(gridBrush, gridStroke);
            if (gridPen.CanFreeze)
            {
                gridPen.Freeze();
            }

            for (double x = gridStep; x < width; x += gridStep)
            {
                double snappedX = (Math.Floor(x * pixelsPerDip) / pixelsPerDip) + halfPixel;
                dc.DrawLine(gridPen, new WpfPoint(snappedX, 0d), new WpfPoint(snappedX, height));
            }

            for (double y = gridStep; y < height; y += gridStep)
            {
                double snappedY = (Math.Floor(y * pixelsPerDip) / pixelsPerDip) + halfPixel;
                dc.DrawLine(gridPen, new WpfPoint(0d, snappedY), new WpfPoint(width, snappedY));
            }
        }

        if (!_geometry.HasGeometry || _orderedMonitors.Count == 0)
        {
            return;
        }

        WpfBrush monitorFill = MonitorFill ?? ResolveBrush("Berth.MonitorFill", WpfBrushes.Transparent);
        WpfBrush monitorStroke = MonitorStroke ?? ResolveBrush("Berth.MonitorStroke", WpfBrushes.Transparent);
        double strokeThickness = ResolveDouble("Berth.MonitorStrokeThickness", 1.5d);
        double monitorRadius = ResolveCornerRadius("Berth.RadiusMonitor", 6d);
        var monitorPen = new WpfPen(monitorStroke, strokeThickness);
        if (monitorPen.CanFreeze)
        {
            monitorPen.Freeze();
        }

        // Pass 1: draw every monitor rectangle first so stacked monitors never paint over a lower monitor's label.
        var rects = new Rect[_orderedMonitors.Count];
        for (int i = 0; i < _orderedMonitors.Count; i++)
        {
            MapRect mr = _geometry.MonitorRect(_orderedMonitors[i]);
            var rect = new Rect(mr.X, mr.Y, Math.Max(0d, mr.Width), Math.Max(0d, mr.Height));
            rects[i] = rect;
            _renderedMonitorRects.Add(rect);
            dc.DrawRoundedRectangle(monitorFill, monitorPen, rect, monitorRadius, monitorRadius);
        }

        // Pass 2: measure and lay out monitor labels together so no two labels overlap and none are ellipsized (SPEC §3).
        WpfFontFamily monoFamily = ResolveFontFamily("Berth.Type.FamilyMono", new WpfFontFamily("Consolas"));
        double monoFontSize = ResolveStyleFontSize("Berth.Type.Mono", 12d);
        WpfBrush labelBrush = LabelBrush ?? ResolveBrush("Berth.TextSecondary", WpfBrushes.Gray);
        double labelGap = ResolveLabelGap();
        var typeface = new WpfTypeface(monoFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double cardContentWidth = Math.Max(1d, width - (2d * _padding));

        var placedBounds = new List<Rect>(_orderedMonitors.Count);
        for (int i = 0; i < _orderedMonitors.Count; i++)
        {
            string text = MapGeometry.FormatMonitorLabel(i + 1, _orderedMonitors[i]);
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                monoFontSize,
                labelBrush,
                pixelsPerDip)
            {
                MaxTextWidth = cardContentWidth,
            };

            double labelWidth = formatted.Width;
            double labelHeight = formatted.Height;
            double labelX = rects[i].X;
            double maxLeft = Math.Max(_padding, width - _padding - labelWidth);
            if (labelX > maxLeft)
            {
                labelX = maxLeft;
            }

            if (labelX < _padding)
            {
                labelX = _padding;
            }

            double preferredY = Math.Clamp(rects[i].Y - labelHeight - labelGap, 0d, Math.Max(0d, height - labelHeight));
            Rect candidate = FindFreeLabelRect(labelX, preferredY, labelWidth, labelHeight, labelGap, height, placedBounds);

            placedBounds.Add(candidate);
            dc.DrawText(formatted, new WpfPoint(candidate.X, candidate.Y));
            _renderedLabels.Add(new RenderedMonitorLabel(i + 1, text, candidate));
        }
    }

    private static Rect FindFreeLabelRect(
        double x,
        double preferredY,
        double width,
        double height,
        double gap,
        double viewportHeight,
        List<Rect> placed)
    {
        double maxY = Math.Max(0d, viewportHeight - height);
        var candidatesY = new List<double> { preferredY };
        foreach (Rect prev in placed)
        {
            if (prev.Right <= x || prev.Left >= x + width)
            {
                continue;
            }

            double above = prev.Top - height - gap;
            if (above >= 0d && above <= maxY)
            {
                candidatesY.Add(above);
            }

            double below = prev.Bottom + gap;
            if (below >= 0d && below <= maxY)
            {
                candidatesY.Add(below);
            }
        }

        // Order by closest distance to preferredY so we pick the nearest non-overlapping vertical interval inside the viewport.
        candidatesY.Sort((a, b) => Math.Abs(a - preferredY).CompareTo(Math.Abs(b - preferredY)));
        foreach (double y in candidatesY)
        {
            var rect = new Rect(x, y, width, height);
            if (!placed.Any(p => p.IntersectsWith(rect)))
            {
                return rect;
            }
        }

        return new Rect(x, Math.Clamp(preferredY, 0d, maxY), width, height);
    }

    private double ResolveLabelGap() =>
        Math.Max(2d, ResolveThicknessTop("Berth.Space.SubtitleTop", 2d) * 2d);

    private WpfBrush ResolveBrush(string key, WpfBrush fallback) =>
        TryFindResource(key) as WpfBrush ?? fallback;

    private double ResolveDouble(string key, double fallback) =>
        TryFindResource(key) is double d && double.IsFinite(d) ? d : fallback;

    private double ResolveCornerRadius(string key, double fallback) =>
        TryFindResource(key) is CornerRadius r && double.IsFinite(r.TopLeft) ? r.TopLeft : fallback;

    private double ResolveThicknessLeft(string key, double fallback) =>
        TryFindResource(key) is Thickness t && double.IsFinite(t.Left) && t.Left > 0d ? t.Left : fallback;

    private double ResolveThicknessTop(string key, double fallback) =>
        TryFindResource(key) is Thickness t && double.IsFinite(t.Top) && t.Top > 0d ? t.Top : fallback;

    private WpfFontFamily ResolveFontFamily(string key, WpfFontFamily fallback) =>
        TryFindResource(key) as WpfFontFamily ?? fallback;

    private double ResolveStyleFontSize(string styleKey, double fallback)
    {
        if (TryFindResource(styleKey) is Style style)
        {
            foreach (SetterBase setterBase in style.Setters)
            {
                if (setterBase is Setter setter
                    && setter.Property == System.Windows.Controls.TextBlock.FontSizeProperty
                    && setter.Value is double size
                    && double.IsFinite(size)
                    && size > 0d)
                {
                    return size;
                }
            }
        }

        return fallback;
    }
}
