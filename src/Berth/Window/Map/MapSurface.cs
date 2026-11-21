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
/// grid, each monitor rectangle to scale, and each monitor label above its top-left corner from theme tokens only.
/// </summary>
internal sealed class MapSurface : FrameworkElement
{
    private const double LabelBottomGap = 4d;

    private IReadOnlyList<MonitorInfo> _monitors = [];
    private MapGeometry _geometry = MapGeometry.Empty;
    private double _padding = 24d;
    private readonly List<RenderedMonitorLabel> _renderedLabels = [];
    private readonly List<Rect> _renderedMonitorRects = [];

    public IReadOnlyList<RenderedMonitorLabel> RenderedLabels => _renderedLabels;

    public IReadOnlyList<Rect> RenderedMonitorRects => _renderedMonitorRects;

    public void Update(IReadOnlyList<MonitorInfo> monitors, MapGeometry geometry, double padding)
    {
        _monitors = monitors;
        _geometry = geometry;
        _padding = padding;
        InvalidateVisual();
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
        WpfBrush gridBrush = ResolveBrush("Berth.MapGrid", WpfBrushes.Transparent);
        if (gridStep > 0d)
        {
            var gridPen = new WpfPen(gridBrush, 1d);
            if (gridPen.CanFreeze)
            {
                gridPen.Freeze();
            }

            for (double x = gridStep; x < width; x += gridStep)
            {
                double snappedX = Math.Floor(x * pixelsPerDip) / pixelsPerDip + halfPixel;
                dc.DrawLine(gridPen, new WpfPoint(snappedX, 0d), new WpfPoint(snappedX, height));
            }

            for (double y = gridStep; y < height; y += gridStep)
            {
                double snappedY = Math.Floor(y * pixelsPerDip) / pixelsPerDip + halfPixel;
                dc.DrawLine(gridPen, new WpfPoint(0d, snappedY), new WpfPoint(width, snappedY));
            }
        }

        if (!_geometry.HasGeometry || _monitors.Count == 0)
        {
            return;
        }

        IReadOnlyList<MonitorInfo> ordered = MonitorNumbering.Order(_monitors);
        WpfBrush monitorFill = ResolveBrush("Berth.MonitorFill", WpfBrushes.Transparent);
        WpfBrush monitorStroke = ResolveBrush("Berth.MonitorStroke", WpfBrushes.Transparent);
        double strokeThickness = ResolveDouble("Berth.MonitorStrokeThickness", 1.5d);
        double monitorRadius = ResolveCornerRadius("Berth.RadiusMonitor", 6d);
        var monitorPen = new WpfPen(monitorStroke, strokeThickness);
        if (monitorPen.CanFreeze)
        {
            monitorPen.Freeze();
        }

        // Pass 1: draw every monitor rectangle first so stacked monitors never paint over a lower monitor's label.
        var rects = new Rect[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            MapRect mr = _geometry.MonitorRect(ordered[i]);
            var rect = new Rect(mr.X, mr.Y, Math.Max(0d, mr.Width), Math.Max(0d, mr.Height));
            rects[i] = rect;
            _renderedMonitorRects.Add(rect);
            dc.DrawRoundedRectangle(monitorFill, monitorPen, rect, monitorRadius, monitorRadius);
        }

        // Pass 2: draw every monitor label above its top-left corner, clamped horizontally inside the padded card.
        WpfFontFamily monoFamily = ResolveFontFamily("Berth.Type.FamilyMono", new WpfFontFamily("Consolas"));
        double monoFontSize = ResolveStyleFontSize("Berth.Type.Mono", 12d);
        WpfBrush labelBrush = ResolveBrush("Berth.TextSecondary", WpfBrushes.Gray);
        var typeface = new WpfTypeface(monoFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        for (int i = 0; i < ordered.Count; i++)
        {
            string text = MapGeometry.FormatMonitorLabel(i + 1, ordered[i]);
            double maxAvailableWidth = Math.Max(1d, width - (2d * _padding));
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                monoFontSize,
                labelBrush,
                pixelsPerDip)
            {
                MaxTextWidth = maxAvailableWidth,
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };

            double labelX = rects[i].X;
            double maxLeft = Math.Max(_padding, width - _padding - formatted.Width);
            if (labelX > maxLeft)
            {
                labelX = maxLeft;
            }

            if (labelX < _padding)
            {
                labelX = _padding;
            }

            double labelY = Math.Max(2d, rects[i].Y - formatted.Height - LabelBottomGap);
            dc.DrawText(formatted, new WpfPoint(labelX, labelY));
            _renderedLabels.Add(new RenderedMonitorLabel(
                i + 1,
                text,
                new Rect(labelX, labelY, formatted.Width, formatted.Height)));
        }
    }

    private WpfBrush ResolveBrush(string key, WpfBrush fallback) =>
        TryFindResource(key) as WpfBrush ?? fallback;

    private double ResolveDouble(string key, double fallback) =>
        TryFindResource(key) is double d && double.IsFinite(d) ? d : fallback;

    private double ResolveCornerRadius(string key, double fallback) =>
        TryFindResource(key) is CornerRadius r && double.IsFinite(r.TopLeft) ? r.TopLeft : fallback;

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
