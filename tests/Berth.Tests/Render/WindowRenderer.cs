using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Berth.App;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSize = System.Windows.Size;
using WpfSystemColors = System.Windows.SystemColors;

namespace Berth.Tests.Render;

internal enum RenderTheme
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// Off-screen renderer for WPF surfaces (SPEC §1-§4, D04 T01 §11, D05 T01 §13): measures, arranges, composites the
/// active Fluent or High Contrast background behind the live element without reparenting it, supports 1.0x / 1.5x
/// scale via <see cref="ScaleTransform"/>, and saves a 96-DPI PNG, restoring the application's theme and transform
/// state even on failure.
/// </summary>
internal static class WindowRenderer
{
    // Deterministic High Contrast stand-in system colours, deliberately disjoint from every hex colour in
    // design/tokens.json (Light and Dark), so High Contrast renders prove no Berth brand/light/dark colour leaked.
    internal static readonly WpfColor HighContrastWindowColor = WpfColor.FromRgb(0x05, 0x05, 0x05);
    internal static readonly WpfColor HighContrastWindowTextColor = WpfColor.FromRgb(0xF5, 0xF5, 0x00);
    internal static readonly WpfColor HighContrastGrayTextColor = WpfColor.FromRgb(0x3F, 0xF2, 0x3F);
    internal static readonly WpfColor HighContrastHighlightColor = WpfColor.FromRgb(0x1A, 0xEB, 0xFF);
    internal static readonly WpfColor HighContrastHighlightTextColor = WpfColor.FromRgb(0x05, 0x05, 0x05);

    public static void Render(
        FrameworkElement root,
        int width,
        int height,
        string path,
        RenderTheme? theme = null,
        double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        BerthApplication? berthApp = WpfApplication.Current as BerthApplication;
#pragma warning disable WPF0001 // WPF ThemeMode is experimental in .NET 11; BerthApplication uses ThemeMode for Light/Dark switching.
        ThemeMode originalThemeMode = berthApp?.ThemeMode ?? ThemeMode.None;
#pragma warning restore WPF0001
        ColorTheme originalColorTheme = berthApp?.CurrentColorTheme ?? ColorTheme.Dark;
        Transform originalLayoutTransform = root.LayoutTransform;
        object originalForeground = root.ReadLocalValue(System.Windows.Documents.TextElement.ForegroundProperty);
        ResourceDictionary? highContrastOverlay = null;
        bool switchedTheme = false;

        try
        {
            if (theme is { } targetTheme && berthApp is not null)
            {
                switchedTheme = true;
                if (targetTheme == RenderTheme.HighContrast)
                {
#pragma warning disable WPF0001
                    berthApp.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
                    highContrastOverlay = CreateHighContrastStandInDictionary();
                    berthApp.Resources.MergedDictionaries.Add(highContrastOverlay);
                    berthApp.ApplyColorTheme(ColorTheme.HighContrast);
                    var hcText = new SolidColorBrush(HighContrastWindowTextColor);
                    hcText.Freeze();
                    System.Windows.Documents.TextElement.SetForeground(root, hcText);
                }
                else
                {
#pragma warning disable WPF0001
                    berthApp.ThemeMode = targetTheme == RenderTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
                    berthApp.ApplyColorTheme(targetTheme == RenderTheme.Light ? ColorTheme.Light : ColorTheme.Dark);
                }
            }

            WpfBrush background = theme == RenderTheme.HighContrast
                ? new SolidColorBrush(HighContrastWindowColor)
                : ResolveFluentWindowBackground();

            int pixelWidth = (int)Math.Round(width * scale);
            int pixelHeight = (int)Math.Round(height * scale);

            if (Math.Abs(scale - 1.0) > 1e-6)
            {
                root.LayoutTransform = new ScaleTransform(scale, scale);
            }

            var targetSize = new WpfSize(pixelWidth, pixelHeight);
            root.Measure(targetSize);
            root.Arrange(new Rect(new System.Windows.Point(0, 0), targetSize));
            root.UpdateLayout();

            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);

            var backdrop = new DrawingVisual();
            using (DrawingContext dc = backdrop.RenderOpen())
            {
                dc.DrawRectangle(background, pen: null, new Rect(0, 0, pixelWidth, pixelHeight));
            }

            bitmap.Render(backdrop);
            bitmap.Render(root);

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }
        finally
        {
            root.LayoutTransform = originalLayoutTransform;
            if (originalForeground == DependencyProperty.UnsetValue)
            {
                root.ClearValue(System.Windows.Documents.TextElement.ForegroundProperty);
            }
            else
            {
                root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, originalForeground);
            }

            if (highContrastOverlay is not null && berthApp is not null)
            {
                berthApp.Resources.MergedDictionaries.Remove(highContrastOverlay);
            }

            if (switchedTheme && berthApp is not null)
            {
#pragma warning disable WPF0001
                berthApp.ThemeMode = originalThemeMode;
#pragma warning restore WPF0001
                berthApp.ApplyColorTheme(originalColorTheme);
            }
        }
    }

    internal static ResourceDictionary CreateHighContrastStandInDictionary()
    {
        var windowBrush = new SolidColorBrush(HighContrastWindowColor);
        var windowTextBrush = new SolidColorBrush(HighContrastWindowTextColor);
        var grayTextBrush = new SolidColorBrush(HighContrastGrayTextColor);
        var highlightBrush = new SolidColorBrush(HighContrastHighlightColor);
        var highlightTextBrush = new SolidColorBrush(HighContrastHighlightTextColor);
        windowBrush.Freeze();
        windowTextBrush.Freeze();
        grayTextBrush.Freeze();
        highlightBrush.Freeze();
        highlightTextBrush.Freeze();

        // Only supply stand-in SystemColors and Fluent chrome brushes; never override Berth.* keys so
        // Tokens.HighContrast.xaml's own DynamicResource bindings are genuinely exercised!
        var dict = new ResourceDictionary
        {
            [WpfSystemColors.WindowColorKey] = HighContrastWindowColor,
            [WpfSystemColors.WindowTextColorKey] = HighContrastWindowTextColor,
            [WpfSystemColors.GrayTextColorKey] = HighContrastGrayTextColor,
            [WpfSystemColors.HighlightColorKey] = HighContrastHighlightColor,
            [WpfSystemColors.HighlightTextColorKey] = HighContrastHighlightTextColor,
            [WpfSystemColors.ControlTextColorKey] = HighContrastWindowTextColor,
            [WpfSystemColors.WindowBrushKey] = windowBrush,
            [WpfSystemColors.WindowTextBrushKey] = windowTextBrush,
            [WpfSystemColors.GrayTextBrushKey] = grayTextBrush,
            [WpfSystemColors.HighlightBrushKey] = highlightBrush,
            [WpfSystemColors.HighlightTextBrushKey] = highlightTextBrush,
            ["ApplicationPageBackgroundThemeBrush"] = windowBrush,
            ["SolidBackgroundFillColorBaseBrush"] = windowBrush,
            ["CardBackgroundFillColorDefaultBrush"] = windowBrush,
            ["CardStrokeColorDefaultBrush"] = windowTextBrush,
            ["DividerStrokeColorDefaultBrush"] = windowTextBrush,
            ["TextFillColorPrimaryBrush"] = windowTextBrush,
            ["TextFillColorSecondaryBrush"] = windowTextBrush,
            ["TextFillColorDisabledBrush"] = grayTextBrush,
            ["ControlFillColorDefaultBrush"] = windowBrush,
            ["ControlFillColorSecondaryBrush"] = windowBrush,
            ["ControlFillColorDisabledBrush"] = windowBrush,
            ["ControlStrokeColorDefaultBrush"] = windowTextBrush,
            ["ControlStrokeColorSecondaryBrush"] = windowTextBrush,
            ["ControlElevationBorderBrush"] = windowTextBrush,
            ["ButtonBackground"] = windowBrush,
            ["ButtonForeground"] = windowTextBrush,
            ["ButtonBorderBrush"] = windowTextBrush,
            ["AccentFillColorDefaultBrush"] = highlightBrush,
            ["AccentFillColorSecondaryBrush"] = highlightBrush,
            ["AccentButtonBackground"] = highlightBrush,
            ["AccentButtonForeground"] = highlightTextBrush,
            ["AccentButtonBorderBrush"] = highlightBrush,
            ["AccentTextFillColorPrimaryBrush"] = highlightBrush,
            ["TextOnAccentFillColorPrimaryBrush"] = highlightTextBrush,
            ["SubtleFillColorSecondaryBrush"] = highlightBrush,
            ["SubtleFillColorTertiaryBrush"] = highlightBrush,
            ["ListViewItemBackgroundSelected"] = highlightBrush,
            ["ListBoxItemSelectedBackground"] = highlightBrush,
            ["FocusStrokeColorOuterBrush"] = highlightBrush,
            ["FocusStrokeColorInnerBrush"] = windowBrush,
            ["ToggleSwitchFillOff"] = windowBrush,
            ["ToggleSwitchStrokeOff"] = windowTextBrush,
            ["ToggleSwitchKnobFillOff"] = windowTextBrush,
            ["ToggleSwitchFillOn"] = highlightBrush,
            ["ToggleSwitchStrokeOn"] = highlightBrush,
            ["ToggleSwitchKnobFillOn"] = highlightTextBrush,
        };
        return dict;
    }

    private static WpfBrush ResolveFluentWindowBackground()
    {
        WpfApplication app = WpfApplication.Current
            ?? throw new InvalidOperationException("WPF Application.Current is not initialized.");

        foreach (string key in new[]
        {
            "ApplicationPageBackgroundThemeBrush",
            "SolidBackgroundFillColorBaseBrush",
            "LayerFillColorDefaultBrush",
        })
        {
            if (app.TryFindResource(key) is WpfBrush brush)
            {
                return brush;
            }
        }

        throw new InvalidOperationException("No Fluent window background brush resource was found in Application.Current.Resources.");
    }
}
