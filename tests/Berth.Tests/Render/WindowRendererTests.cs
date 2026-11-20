using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Berth.App;
using Berth.Tests.App;
using Berth.Window;
using WpfApplication = System.Windows.Application;

namespace Berth.Tests.Render;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "Render")]
public sealed class WindowRendererTests(AppFixture appFixture)
{
    [Fact]
    public void RendersMainWindowFrameInLightAndDarkThemesWithDistinctBrightnessAndDivider()
    {
        string outputDir = ResolveRenderOutputDirectory();
        string lightPath = Path.Combine(outputDir, "frame-light.png");
        string darkPath = Path.Combine(outputDir, "frame-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow();
            try
            {
                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        RenderedImage light = LoadPng(lightPath);
        RenderedImage dark = LoadPng(darkPath);

        Assert.Equal(980, light.Width);
        Assert.Equal(640, light.Height);
        Assert.Equal(980, dark.Width);
        Assert.Equal(640, dark.Height);

        // Both captures must be opaque, non-uniform, and show an exact 1 px divider at x = 250 (x=249 and x=251 are background).
        Assert.Equal(255, light.AlphaAt(100, 100));
        Assert.Equal(255, dark.AlphaAt(100, 100));
        Assert.Equal(light.PixelAt(249, 100), light.PixelAt(251, 100));
        Assert.NotEqual(light.PixelAt(249, 100), light.PixelAt(250, 100));
        Assert.Equal(dark.PixelAt(249, 100), dark.PixelAt(251, 100));
        Assert.NotEqual(dark.PixelAt(249, 100), dark.PixelAt(250, 100));

        // Light and Dark themes must differ widely in average brightness.
        double lightLuma = light.AverageLuminance();
        double darkLuma = dark.AverageLuminance();
        Assert.True(
            lightLuma - darkLuma > 100.0,
            $"Expected Light luminance ({lightLuma:F1}) to exceed Dark luminance ({darkLuma:F1}) by > 100.");
    }

    [Fact]
    public void RenderHonorsNonDefaultDimensionsAndArrangesDividerToBottomRow()
    {
        using var data = new TestData();
        string customPath = Path.Combine(data.Root, "frame-1100x700.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow();
            try
            {
                WindowRenderer.Render(window.RootGrid, 1100, 700, customPath, RenderTheme.Dark);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        RenderedImage img = LoadPng(customPath);
        Assert.Equal(1100, img.Width);
        Assert.Equal(700, img.Height);

        // Divider at x = 250 must reach the last row (y = 699), proving layout arranged at 1100x700 rather than 980x640.
        Assert.NotEqual(img.PixelAt(100, 699), img.PixelAt(250, 699));
    }

    [Fact]
    public void RenderRestoresThemeModeColorThemeAndBrushDictionaryAfterSuccessAndAfterWriteFailure()
    {
        using var data = new TestData();
        string validPath = Path.Combine(data.Root, "ok.png");
        string failingPath = Path.Combine(data.Root, "blocked-dir");
        Directory.CreateDirectory(failingPath);

        appFixture.Invoke(() =>
        {
            var app = Assert.IsType<BerthApplication>(WpfApplication.Current);
#pragma warning disable WPF0001
            ThemeMode beforeMode = app.ThemeMode;
#pragma warning restore WPF0001
            ColorTheme beforeColor = app.CurrentColorTheme;
            System.Windows.Media.Color beforeMapColor = ((SolidColorBrush)app.Resources["Berth.MapBackground"]).Color;

            var window = new MainWindow();
            try
            {
                // 1. Success path with opposite theme.
                RenderTheme opposite = beforeColor == ColorTheme.Light ? RenderTheme.Dark : RenderTheme.Light;
                WindowRenderer.Render(window.RootGrid, 980, 640, validPath, opposite);

#pragma warning disable WPF0001
                Assert.Equal(beforeMode, app.ThemeMode);
#pragma warning restore WPF0001
                Assert.Equal(beforeColor, app.CurrentColorTheme);
                Assert.Equal(beforeMapColor, ((SolidColorBrush)app.Resources["Berth.MapBackground"]).Color);

                // 2. Forced write failure (path is an existing directory) still restores all theme state.
                Assert.ThrowsAny<Exception>(() => WindowRenderer.Render(window.RootGrid, 980, 640, failingPath, opposite));

#pragma warning disable WPF0001
                Assert.Equal(beforeMode, app.ThemeMode);
#pragma warning restore WPF0001
                Assert.Equal(beforeColor, app.CurrentColorTheme);
                Assert.Equal(beforeMapColor, ((SolidColorBrush)app.Resources["Berth.MapBackground"]).Color);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    private static string ResolveRenderOutputDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable("BERTH_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return envDir;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Berth.slnx")))
        {
            dir = dir.Parent;
        }

        string repoRoot = dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Berth.slnx above test output.");
        return Path.Combine(repoRoot, "build", "renders");
    }

    private static RenderedImage LoadPng(string path)
    {
        Assert.True(File.Exists(path), $"Expected rendered PNG at '{path}'.");
        using FileStream fs = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0);
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        return new RenderedImage(width, height, stride, pixels);
    }

    private sealed class RenderedImage(int width, int height, int stride, byte[] pixels)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;

        public uint PixelAt(int x, int y)
        {
            int offset = (y * stride) + (x * 4);
            return BitConverter.ToUInt32(pixels, offset);
        }

        public byte AlphaAt(int x, int y) => pixels[(y * stride) + (x * 4) + 3];

        public double AverageLuminance()
        {
            double sum = 0;
            int count = Width * Height;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                sum += (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            }

            return sum / count;
        }
    }
}
