using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Window.Map;

/// <summary>
/// Position map control (SPEC §7): draws monitors to scale with labels on <see cref="MapSurface"/> and plots each
/// <see cref="ComparedIcon"/> as a <see cref="System.Windows.Controls.ListBoxItem"/> positioned on a <see cref="System.Windows.Controls.Canvas"/>.
/// </summary>
internal sealed partial class PositionMap : System.Windows.Controls.UserControl
{
    internal const double LabelHeight = 20d;

    private readonly ObservableCollection<MapMarkerItem> _items = [];
    private MainViewModel? _viewModel;
    private MapGeometry _geometry = MapGeometry.Empty;

    public PositionMap()
    {
        InitializeComponent();
        MarkersList.ItemsSource = _items;
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RebuildLayout();
    }

    internal MapGeometry CurrentGeometry => _geometry;

    internal ObservableCollection<MapMarkerItem> MarkerItems => _items;

    /// <summary>Forces the custom surface to re-resolve brushes and typeface after a live theme change.</summary>
    public void RefreshThemeVisuals() => Surface.InvalidateVisual();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildItemsAndLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Comparison)
            or nameof(MainViewModel.MapMonitors)
            or nameof(MainViewModel.Selected)
            or "")
        {
            RebuildItemsAndLayout();
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildLayout();
    }

    private void RebuildItemsAndLayout()
    {
        _items.Clear();

        LayoutComparison? comparison = _viewModel?.Comparison;
        IReadOnlyList<MonitorInfo> monitors = _viewModel?.MapMonitors ?? [];

        if (comparison is not null && monitors.Count > 0)
        {
            for (int i = 0; i < comparison.Icons.Count; i++)
            {
                ComparedIcon icon = comparison.Icons[i];
                ClampedDesktopPoint clamped = MapGeometry.Clamp(icon.Saved.X, icon.Saved.Y, monitors);
                _items.Add(new MapMarkerItem(icon, zIndex: i, offScreen: icon.OffScreen || clamped.OffScreen));
            }
        }

        RebuildLayout();
    }

    private void RebuildLayout()
    {
        IReadOnlyList<MonitorInfo> monitors = _viewModel?.MapMonitors ?? [];
        double width = MapViewport.ActualWidth;
        double height = MapViewport.ActualHeight;
        double padding = ResolveDouble("Berth.MapPadding", 24d);
        double targetSize = ResolveDouble("Berth.MarkerTarget", 19d);

        _geometry = MapGeometry.Fit(monitors, width, height, padding, LabelHeight);
        Surface.Update(monitors, _geometry, padding);

        if (!_geometry.HasGeometry || monitors.Count == 0)
        {
            return;
        }

        foreach (MapMarkerItem item in _items)
        {
            ClampedDesktopPoint clamped = MapGeometry.Clamp(item.Icon.Saved.X, item.Icon.Saved.Y, monitors);
            MapPoint pt = _geometry.ToPanel(clamped.X, clamped.Y);
            item.UpdatePosition(pt.X, pt.Y, targetSize);
        }
    }

    private double ResolveDouble(string key, double fallback) =>
        TryFindResource(key) is double d && double.IsFinite(d) ? d : fallback;
}
