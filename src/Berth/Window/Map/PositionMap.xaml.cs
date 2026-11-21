using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Berth.Core;
using Berth.Core.Model;
using WpfBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;

namespace Berth.Window.Map;

/// <summary>
/// Position map control (SPEC §7): draws monitors to scale with labels and connector/drop lines on
/// <see cref="MapSurface"/>, plots each <see cref="ComparedIcon"/> as a <see cref="ListBoxItem"/> on a
/// <see cref="Canvas"/>, and maintains the bottom-left readout and bottom-right count.
/// </summary>
internal sealed partial class PositionMap : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<MapMarkerItem> _items = [];
    private IReadOnlyList<MonitorInfo> _orderedMonitors = [];
    private MainViewModel? _viewModel;
    private MapGeometry _geometry = MapGeometry.Empty;
    private string? _lastLayoutKey;
    private string? _lastSelectedIconId;
    private int _lastSelectedIndex = -1;
    private bool _pendingFocusAfterTransientNull;
    private bool _isRebuildingItems;

    public PositionMap()
    {
        InitializeComponent();
        MarkersList.ItemsSource = _items;
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RebuildLayout();
    }

    internal MapGeometry CurrentGeometry => _geometry;

    internal ObservableCollection<MapMarkerItem> MarkerItems => _items;

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
            or nameof(MainViewModel.ComparisonKind)
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

    private void OnFooterSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildLayout();
    }

    private void OnMarkersSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRebuildingItems)
        {
            return;
        }

        UpdateFooterAndSurfaceOverlays();
    }

    private void OnMarkersPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_items.Count == 0)
        {
            return;
        }

        int current = MarkersList.SelectedIndex;
        int next = e.Key switch
        {
            System.Windows.Input.Key.Down or System.Windows.Input.Key.Right =>
                current < 0 ? 0 : Math.Min(_items.Count - 1, current + 1),
            System.Windows.Input.Key.Up or System.Windows.Input.Key.Left =>
                current < 0 ? _items.Count - 1 : Math.Max(0, current - 1),
            System.Windows.Input.Key.Home => 0,
            System.Windows.Input.Key.End => _items.Count - 1,
            _ => -1,
        };

        if (next < 0)
        {
            return;
        }

        MarkersList.SelectedIndex = next;
        if (MarkersList.ItemContainerGenerator.ContainerFromIndex(next) is System.Windows.Controls.ListBoxItem container)
        {
            container.Focus();
        }

        e.Handled = true;
    }

    private void RebuildItemsAndLayout()
    {
        string? newLayoutKey = _viewModel?.Selected?.Key;
        LayoutComparison? comparison = _viewModel?.Comparison;
        string? currentSelectedIconId = (MarkersList.SelectedItem as MapMarkerItem)?.Icon.Saved.Id;
        int currentSelectedIndex = MarkersList.SelectedIndex;
        bool hadKeyboardFocus = MarkersList.IsKeyboardFocusWithin || _pendingFocusAfterTransientNull;

        if (newLayoutKey is null || comparison is null)
        {
            if (currentSelectedIconId is not null)
            {
                _lastSelectedIconId = currentSelectedIconId;
                _lastSelectedIndex = currentSelectedIndex;
            }

            if (MarkersList.IsKeyboardFocusWithin)
            {
                _pendingFocusAfterTransientNull = true;
            }

            _isRebuildingItems = true;
            try
            {
                _items.Clear();
                _orderedMonitors = [];
                MarkersList.SelectedItem = null;
            }
            finally
            {
                _isRebuildingItems = false;
            }

            RebuildLayout();
            return;
        }

        _pendingFocusAfterTransientNull = false;
        string? previouslySelectedIconId = currentSelectedIconId ?? _lastSelectedIconId;
        int previousSelectedIndex = currentSelectedIndex >= 0 ? currentSelectedIndex : _lastSelectedIndex;
        bool sameLayout = _lastLayoutKey is not null
            && string.Equals(_lastLayoutKey, newLayoutKey, StringComparison.Ordinal);

        MapMarkerItem? restoredSelection = null;
        _isRebuildingItems = true;
        try
        {
            _items.Clear();

            IReadOnlyList<MonitorInfo> monitors = _viewModel?.MapMonitors ?? [];
            _orderedMonitors = monitors.Count > 0 ? MonitorNumbering.Order(monitors) : [];

            if (comparison is not null && _orderedMonitors.Count > 0)
            {
                for (int i = 0; i < comparison.Icons.Count; i++)
                {
                    ComparedIcon icon = comparison.Icons[i];
                    ClampedDesktopPoint clamped = MapGeometry.ClampOrdered(icon.Saved.X, icon.Saved.Y, _orderedMonitors);
                    var marker = new MapMarkerItem(icon, zIndex: i, clamped);
                    _items.Add(marker);

                    if (sameLayout
                        && restoredSelection is null
                        && previouslySelectedIconId is not null
                        && string.Equals(icon.Saved.Id, previouslySelectedIconId, StringComparison.OrdinalIgnoreCase))
                    {
                        restoredSelection = marker;
                    }
                }
            }

            if (restoredSelection is null && hadKeyboardFocus && sameLayout && _items.Count > 0)
            {
                int fallbackIndex = previousSelectedIndex >= 0
                    ? Math.Min(previousSelectedIndex, _items.Count - 1)
                    : 0;
                restoredSelection = _items[fallbackIndex];
            }

            _lastLayoutKey = newLayoutKey;
            _lastSelectedIconId = restoredSelection?.Icon.Saved.Id;
            _lastSelectedIndex = restoredSelection is not null ? _items.IndexOf(restoredSelection) : -1;
            MarkersList.SelectedItem = restoredSelection;
        }
        finally
        {
            _isRebuildingItems = false;
        }

        RebuildLayout();

        if (hadKeyboardFocus)
        {
            if (restoredSelection is not null)
            {
                MarkersList.UpdateLayout();
                if (MarkersList.ItemContainerGenerator.ContainerFromItem(restoredSelection) is ListBoxItem container)
                {
                    container.Focus();
                }
                else
                {
                    MapMarkerItem target = restoredSelection;
                    _ = Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Loaded,
                        new Action(() => TryRestoreDeferredFocus(target)));
                }
            }
            else if (_items.Count == 0)
            {
                // Focused marker disappeared and map is now empty: move focus cleanly so it is never stranded.
                _ = MarkersList.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            }
        }
    }

    internal void TryRestoreDeferredFocus(MapMarkerItem target)
    {
        // Never steal focus back if the user has already moved keyboard focus outside the map.
        if (System.Windows.Input.Keyboard.FocusedElement is not null
            && !MarkersList.IsKeyboardFocusWithin)
        {
            return;
        }

        if (ReferenceEquals(MarkersList.SelectedItem, target)
            && MarkersList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem deferred)
        {
            deferred.Focus();
        }
    }

    private void RebuildLayout()
    {
        double width = MapViewport.ActualWidth;
        double height = MapViewport.ActualHeight;
        double padding = ResolveDouble("Berth.MapPadding", 24d);
        double targetSize = ResolveDouble("Berth.MarkerTarget", 19d);
        double ringSize = ResolveDouble("Berth.MarkerSelectedRing", 23d);
        double strokeThickness = ResolveDouble("Berth.MonitorStrokeThickness", 1.5d);
        double borderInset = (ringSize / 2d) + strokeThickness;
        double labelHeight = Surface.MeasureLabelReserve();

        _geometry = MapGeometry.Fit(_orderedMonitors, width, height, padding, labelHeight);

        if (_geometry.HasGeometry && _orderedMonitors.Count > 0)
        {
            foreach (MapMarkerItem item in _items)
            {
                MapPoint pt = _geometry.ToPanel(item.Clamped.X, item.Clamped.Y);
                double cx = pt.X;
                double cy = pt.Y;

                // Keep markers cleanly inside their monitor rectangle so top-row, edge, and clamped off-screen icons
                // never kiss or protrude past the monitor border.
                if (item.Icon.MonitorNumber >= 1 && item.Icon.MonitorNumber <= _orderedMonitors.Count)
                {
                    MapRect monRect = _geometry.MonitorRect(_orderedMonitors[item.Icon.MonitorNumber - 1]);
                    if (monRect.Width > borderInset * 2d)
                    {
                        cx = Math.Clamp(cx, monRect.X + borderInset, monRect.X + monRect.Width - borderInset);
                    }

                    if (monRect.Height > borderInset * 2d)
                    {
                        cy = Math.Clamp(cy, monRect.Y + borderInset, monRect.Y + monRect.Height - borderInset);
                    }
                }

                item.UpdatePosition(cx, cy, targetSize);
            }
        }

        UpdateFooterAndSurfaceOverlays();
    }

    private void UpdateFooterAndSurfaceOverlays()
    {
        MapMarkerItem? selected = MarkersList.SelectedItem as MapMarkerItem;
        ReadoutLines readout = IconReadout.For(selected?.Icon);

        ReadoutPrimaryText.Text = readout.Primary;
        if (readout.HasSelection)
        {
            ReadoutPrimaryText.ClearValue(TextBlock.ForegroundProperty);
            ReadoutSecondaryText.Text = readout.Secondary;
            ReadoutSecondaryText.Visibility = Visibility.Visible;
        }
        else
        {
            ReadoutPrimaryText.SetResourceReference(TextBlock.ForegroundProperty, "Berth.TextSecondary");
            ReadoutSecondaryText.Text = " ";
            ReadoutSecondaryText.Visibility = Visibility.Hidden;
        }

        CountTextBlock.Text = IconReadout.CountText(_viewModel?.Comparison, _viewModel?.ComparisonKind ?? ComparisonKind.None);

        double padding = ResolveDouble("Berth.MapPadding", 24d);
        var movedLines = new List<(WpfPoint From, WpfPoint To)>();
        if (_geometry.HasGeometry)
        {
            foreach (MapMarkerItem item in _items)
            {
                if (item.State == IconState.Moved && item.Icon.Current is { } current)
                {
                    // SPEC §7: moved line goes from the saved marker to where the icon is now (unclamped, clipped by card).
                    MapPoint livePt = _geometry.ToPanel(current.X, current.Y);
                    movedLines.Add((new WpfPoint(item.CenterX, item.CenterY), new WpfPoint(livePt.X, livePt.Y)));
                }
            }
        }

        (WpfPoint From, WpfPoint To)? dropLine = null;
        if (selected is not null && _geometry.HasGeometry && MapViewport.ActualHeight > 0d)
        {
            double ringHalf = ResolveDouble("Berth.MarkerSelectedRing", 23d) / 2d;
            double startY = Math.Min(MapViewport.ActualHeight, selected.CenterY + ringHalf);
            dropLine = (new WpfPoint(selected.CenterX, startY), new WpfPoint(selected.CenterX, MapViewport.ActualHeight));
        }

        Surface.Update(_orderedMonitors, _geometry, padding, movedLines, dropLine);
    }

    private double ResolveDouble(string key, double fallback) =>
        TryFindResource(key) is double d && double.IsFinite(d) ? d : fallback;
}
