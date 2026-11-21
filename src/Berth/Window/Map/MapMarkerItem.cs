using Berth.Core;
using Berth.Window.Mvvm;

namespace Berth.Window.Map;

/// <summary>One plotted icon marker in <see cref="PositionMap"/> positioned on the map canvas (SPEC §7).</summary>
internal sealed class MapMarkerItem : ObservableObject
{
    private double _left;
    private double _top;
    private double _centerX;
    private double _centerY;

    public MapMarkerItem(ComparedIcon icon, int zIndex, ClampedDesktopPoint clamped)
    {
        Icon = icon;
        ZIndex = zIndex;
        Clamped = clamped;
        OffScreen = icon.OffScreen || clamped.OffScreen;
    }

    public ComparedIcon Icon { get; }

    public int ZIndex { get; }

    public ClampedDesktopPoint Clamped { get; }

    public bool OffScreen { get; }

    public string DisplayName => Icon.Saved.Name;

    public double Left
    {
        get => _left;
        private set => Set(ref _left, value);
    }

    public double Top
    {
        get => _top;
        private set => Set(ref _top, value);
    }

    public double CenterX
    {
        get => _centerX;
        private set => Set(ref _centerX, value);
    }

    public double CenterY
    {
        get => _centerY;
        private set => Set(ref _centerY, value);
    }

    public void UpdatePosition(double centerX, double centerY, double targetSize)
    {
        CenterX = centerX;
        CenterY = centerY;
        Left = centerX - (targetSize / 2d);
        Top = centerY - (targetSize / 2d);
    }
}
