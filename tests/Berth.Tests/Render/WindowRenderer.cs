using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Berth.App;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Render;

internal enum RenderTheme
{
    Light,
    Dark,
}

/// <summary>
/// Off-screen renderer for WPF surfaces (SPEC §1-§4, D04 T01 §11): measures, arranges, composites the active Fluent
/// page background behind the live element without reparenting it, and saves a 96-DPI PNG, restoring the application's
/// theme state even on failure.
/// </summary>
internal static class WindowRenderer
{
    public static void Render(
        FrameworkElement root,
        int width,
        int height,
        string path,
        RenderTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BerthApplication? berthApp = WpfApplication.Current as BerthApplication;
#pragma warning disable WPF0001 // WPF ThemeMode is experimental in .NET 11; BerthApplication uses ThemeMode for Light/Dark switching.
        ThemeMode originalThemeMode = berthApp?.ThemeMode ?? ThemeMode.None;
#pragma warning restore WPF0001
        ColorTheme originalColorTheme = berthApp?.CurrentColorTheme ?? ColorTheme.Dark;
        bool switchedTheme = false;

        try
        {
            if (theme is { } targetTheme && berthApp is not null)
            {
                switchedTheme = true;
#pragma warning disable WPF0001
                berthApp.ThemeMode = targetTheme == RenderTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
                berthApp.ApplyColorTheme(targetTheme == RenderTheme.Light ? ColorTheme.Light : ColorTheme.Dark);
            }

            WpfBrush background = ResolveFluentWindowBackground();

            var targetSize = new WpfSize(width, height);
            root.Measure(targetSize);
            root.Arrange(new Rect(new System.Windows.Point(0, 0), targetSize));
            root.UpdateLayout();

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

            var backdrop = new DrawingVisual();
            using (DrawingContext dc = backdrop.RenderOpen())
            {
                dc.DrawRectangle(background, pen: null, new Rect(0, 0, width, height));
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
            if (switchedTheme && berthApp is not null)
            {
#pragma warning disable WPF0001
                berthApp.ThemeMode = originalThemeMode;
#pragma warning restore WPF0001
                berthApp.ApplyColorTheme(originalColorTheme);
            }
        }
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
