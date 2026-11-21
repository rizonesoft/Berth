using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Berth.Core;
using WpfSize = System.Windows.Size;

namespace Berth.Window.Map;

/// <summary>
/// Centers children without layout-clipping an oversized selection ring (23 px) inside the 19 px marker slot (SPEC §7).
/// </summary>
internal sealed class UnclippedMarkerHost : Grid
{
    protected override WpfSize MeasureOverride(WpfSize constraint)
    {
        var unconstrained = new WpfSize(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(unconstrained);
        }

        double w = double.IsFinite(constraint.Width) ? constraint.Width : 0d;
        double h = double.IsFinite(constraint.Height) ? constraint.Height : 0d;
        return new WpfSize(w, h);
    }

    protected override WpfSize ArrangeOverride(WpfSize arrangeSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            WpfSize desired = child.DesiredSize;
            double x = (arrangeSize.Width - desired.Width) / 2d;
            double y = (arrangeSize.Height - desired.Height) / 2d;
            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
        }

        return arrangeSize;
    }

    protected override Geometry? GetLayoutClip(WpfSize layoutSlotSize) => null;
}

/// <summary>Selects the SPEC §7 marker template by <see cref="IconState"/>.</summary>
internal sealed class MarkerTemplateSelector : DataTemplateSelector
{
    public DataTemplate? InPlaceTemplate { get; set; }

    public DataTemplate? MovedTemplate { get; set; }

    public DataTemplate? MissingTemplate { get; set; }

    public DataTemplate? NeutralTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => item switch
    {
        MapMarkerItem { State: IconState.InPlace } => InPlaceTemplate,
        MapMarkerItem { State: IconState.Moved } => MovedTemplate,
        MapMarkerItem { State: IconState.Missing } => MissingTemplate,
        MapMarkerItem { State: IconState.Neutral } => NeutralTemplate,
        _ => base.SelectTemplate(item, container),
    };
}

/// <summary>Extracts <see cref="CornerRadius.TopLeft"/> as a <see cref="double"/> for <c>Rectangle.RadiusX/RadiusY</c>.</summary>
internal sealed class CornerRadiusToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CornerRadius cr ? cr.TopLeft : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
