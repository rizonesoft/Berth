using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using DrawingColor = System.Drawing.Color;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Release;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "Docs")]
public sealed class DocsRenderTests(AppFixture appFixture)
{
    private const int OuterWidth = 980;
    private const int OuterHeight = 640;
    private const uint PwRenderFullContent = 0x00000002;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaUseImmersiveDarkMode = 20;

    private static readonly MonitorInfo MonPrimary = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonSecondary = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonLaptop = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonUltrawide = new(0, 0, 3440, 1440, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 21, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenderMainWindowDocsScreenshotsInLightAndDark()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        IconPosition[] dockedSaved = BuildSampleIcons(42);
        IconPosition[] laptopSaved = BuildSampleIcons(28, monitor2Count: 0);
        IconPosition[] ultrawideSaved = BuildSampleIcons(42, monitor2Count: 0);

        string dockedKey = ArrangementKey.From([MonPrimary, MonSecondary]);
        string laptopKey = ArrangementKey.From([MonLaptop]);
        string ultrawideKey = ArrangementKey.From([MonUltrawide]);

        store.Save(new Layout(Layout.CurrentSchemaVersion, dockedKey, "Docked", FixedNow, [MonPrimary, MonSecondary], [.. dockedSaved], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, laptopKey, "Laptop", FixedNow.AddDays(-2), [MonLaptop], [.. laptopSaved], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, ultrawideKey, "Studio ultrawide", FixedNow.AddDays(-5), [MonUltrawide], [.. ultrawideSaved], null));

        // 39 in place, 2 moved, 1 missing so the screenshot shows a realistic live comparison.
        var liveIcons = new List<IconPosition>(dockedSaved.Length);
        for (int i = 0; i < dockedSaved.Length; i++)
        {
            if (i == 12)
            {
                continue; // 1 missing
            }

            if (i is 5 or 30)
            {
                liveIcons.Add(dockedSaved[i] with { X = dockedSaved[i].X + 180, Y = dockedSaved[i].Y + 90 });
            }
            else
            {
                liveIcons.Add(dockedSaved[i]);
            }
        }

        var monitors = new FakeMonitors(MonPrimary, MonSecondary);
        var desktop = new FakeDesktopIcons([.. liveIcons]);
        var clock = new FixedClock(FixedNow);
        var log = new Log(paths, clock);
        var settingsStore = new SettingsStore(paths);
        _ = settingsStore.Update(s => s with { RestoreAutomatically = true });
        var runValue = new RunValue(@"Software\Berth.Tests\DocsRender");
        var settingChanges = new SettingChanges(settingsStore, log);
        var service = new LayoutService(desktop, monitors, store, paths, new OperationLock(paths, TimeSpan.FromSeconds(5)), log, clock);
        var vm = new MainViewModel(
            store,
            desktop,
            monitors,
            clock,
            marshalToUiAsync: action =>
            {
                appFixture.Invoke(action);
                return Task.CompletedTask;
            },
            service: service,
            timeZone: TimeZoneInfo.Utc,
            log: log,
            settingsStore: settingsStore,
            runValue: runValue,
            settingChanges: settingChanges);

        await vm.RefreshAsync();

        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("Docked", vm.Selected?.Name);
        Assert.Equal(39, vm.Comparison?.InPlace);
        Assert.Equal(2, vm.Comparison?.Moved);
        Assert.Equal(1, vm.Comparison?.Missing);
        Assert.NotNull(vm.Settings);
        Assert.True(vm.Settings!.RestoreAutomatically);

        string outputDir = ResolveOutputDirectory(data.Root);
        string lightPath = Path.Combine(outputDir, "main-window-light.png");
        string darkPath = Path.Combine(outputDir, "main-window-dark.png");
        string scratchDir = Path.Combine(data.Root, "client-renders");
        Directory.CreateDirectory(scratchDir);
        string iconPath = ResolveRepoFile("assets", "icon", "berth.ico");

        appFixture.Invoke(() =>
        {
            CaptureFramedWindow(vm, lightPath, Path.Combine(scratchDir, "client-light.png"), iconPath, RenderTheme.Light);
            CaptureFramedWindow(vm, darkPath, Path.Combine(scratchDir, "client-dark.png"), iconPath, RenderTheme.Dark);
        });

        byte[] lightBytes = AssertValid980x640Png(lightPath);
        byte[] darkBytes = AssertValid980x640Png(darkPath);
        Assert.False(lightBytes.AsSpan().SequenceEqual(darkBytes), "Light and dark screenshots must differ.");
    }

    private static void CaptureFramedWindow(
        MainViewModel vm,
        string outputPath,
        string clientTempPath,
        string iconPath,
        RenderTheme theme)
    {
        var app = (Berth.App.BerthApplication)System.Windows.Application.Current;
#pragma warning disable WPF0001
        System.Windows.ThemeMode originalMode = app.ThemeMode;
#pragma warning restore WPF0001
        Berth.App.ColorTheme originalColor = app.CurrentColorTheme;

        var window = new MainWindow
        {
            DataContext = vm,
            Width = OuterWidth,
            Height = OuterHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 40,
            Top = 40,
            ShowInTaskbar = false,
            ShowActivated = false,
        };

        try
        {
#pragma warning disable WPF0001
            app.ThemeMode = theme == RenderTheme.Dark ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;
#pragma warning restore WPF0001
            app.ApplyColorTheme(theme == RenderTheme.Dark ? Berth.App.ColorTheme.Dark : Berth.App.ColorTheme.Light);

            window.Show();
            nint hwnd = new WindowInteropHelper(window).Handle;
            int darkFlag = theme == RenderTheme.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int));

            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            // Measure the live framed window's actual client size and client origin relative to the visible outer frame.
            Assert.True(GetWindowRect(hwnd, out RECT winRect), "GetWindowRect failed for MainWindow.");
            Assert.True(GetClientRect(hwnd, out RECT clientRect), "GetClientRect failed for MainWindow.");
            var clientOrigin = new POINT { X = 0, Y = 0 };
            Assert.True(ClientToScreen(hwnd, ref clientOrigin), "ClientToScreen failed for MainWindow.");
            RECT outerRef = winRect;
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out RECT extRect, Marshal.SizeOf<RECT>()) == 0
                && (extRect.Right - extRect.Left) >= clientRect.Right
                && (extRect.Bottom - extRect.Top) >= clientRect.Bottom)
            {
                outerRef = extRect;
            }

            DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            int measuredClientW = Math.Clamp((int)Math.Round(clientRect.Right / dpi.DpiScaleX), 800, OuterWidth);
            int measuredClientH = Math.Clamp((int)Math.Round(clientRect.Bottom / dpi.DpiScaleY), 500, OuterHeight - 24);
            int measuredClientLeft = Math.Clamp((int)Math.Round((clientOrigin.X - outerRef.Left) / dpi.DpiScaleX), 0, Math.Max(0, OuterWidth - measuredClientW));
            int measuredClientTop = Math.Clamp((int)Math.Round((clientOrigin.Y - outerRef.Top) / dpi.DpiScaleY), 24, Math.Max(24, OuterHeight - measuredClientH));

            // Verify live WPF RootGrid actually laid out inside the measured client bounds.
            Assert.InRange(window.RootGrid.ActualWidth, measuredClientW - 4, measuredClientW + 4);
            Assert.InRange(window.RootGrid.ActualHeight, measuredClientH - 4, measuredClientH + 4);

            // Try live native DWM PrintWindow first (works on an unlocked interactive desktop).
            if (TryCaptureLivePrintWindow(hwnd, winRect, clientRect, outputPath))
            {
                RecordCaptureMode(outputPath, "PrintWindow");
                return;
            }

            if (string.Equals(Environment.GetEnvironmentVariable("BERTH_DOCS_REQUIRE_NATIVE"), "1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"BERTH_DOCS_REQUIRE_NATIVE=1 was set, but live PrintWindow returned a blank DWM surface for {Path.GetFileName(outputPath)}.");
            }

            // Locked/headless desktops return a 1-color blank DWM surface from PrintWindow; render the live
            // MainWindow's RootGrid at its exact measured client dimensions and origin inside the 980x640 frame.
            window.RootGrid.Measure(new WpfSize(measuredClientW, measuredClientH));
            window.RootGrid.Arrange(new Rect(0, 0, measuredClientW, measuredClientH));
            window.RootGrid.UpdateLayout();
            WindowRenderer.Render(window.RootGrid, measuredClientW, measuredClientH, clientTempPath, theme);
            CompositeMeasuredFrame(clientTempPath, iconPath, outputPath, measuredClientW, measuredClientH, measuredClientLeft, measuredClientTop, theme == RenderTheme.Dark);
            RecordCaptureMode(outputPath, "MeasuredClientComposite");
        }
        finally
        {
            window.CloseForShutdown();
#pragma warning disable WPF0001
            app.ThemeMode = originalMode;
#pragma warning restore WPF0001
            app.ApplyColorTheme(originalColor);
        }
    }

    private static void RecordCaptureMode(string outputPath, string mode)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            File.AppendAllText(Path.Combine(dir, "capture-modes.txt"), $"{Path.GetFileName(outputPath)}={mode}\n");
        }
    }

    private static bool TryCaptureLivePrintWindow(nint hwnd, RECT winRect, RECT clientRect, string outputPath)
    {
        int fullW = Math.Max(1, winRect.Right - winRect.Left);
        int fullH = Math.Max(1, winRect.Bottom - winRect.Top);

        using var fullBitmap = new Bitmap(fullW, fullH, PixelFormat.Format32bppArgb);
        bool printed;
        using (Graphics g = Graphics.FromImage(fullBitmap))
        {
            nint hdc = g.GetHdc();
            try
            {
                printed = PrintWindow(hwnd, hdc, PwRenderFullContent);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        if (!printed)
        {
            return false;
        }

        Rectangle crop = new(0, 0, fullW, fullH);
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out RECT frameRect, Marshal.SizeOf<RECT>()) == 0)
        {
            int fw = frameRect.Right - frameRect.Left;
            int fh = frameRect.Bottom - frameRect.Top;
            if (fw >= clientRect.Right && fh >= clientRect.Bottom)
            {
                int cx = Math.Clamp(frameRect.Left - winRect.Left, 0, fullW - 1);
                int cy = Math.Clamp(frameRect.Top - winRect.Top, 0, fullH - 1);
                int cw = Math.Clamp(fw, 1, fullW - cx);
                int ch = Math.Clamp(fh, 1, fullH - cy);
                crop = new Rectangle(cx, cy, cw, ch);
            }
        }

        using var output = new Bitmap(OuterWidth, OuterHeight, PixelFormat.Format32bppArgb);
        using (Graphics og = Graphics.FromImage(output))
        {
            og.InterpolationMode = InterpolationMode.HighQualityBicubic;
            og.CompositingQuality = CompositingQuality.HighQuality;
            og.SmoothingMode = SmoothingMode.HighQuality;
            og.PixelOffsetMode = PixelOffsetMode.HighQuality;
            og.DrawImage(fullBitmap, new Rectangle(0, 0, OuterWidth, OuterHeight), crop, GraphicsUnit.Pixel);
        }

        HashSet<int> distinctColors = [];
        for (int y = 40; y < OuterHeight - 20; y += 20)
        {
            for (int x = 20; x < OuterWidth - 20; x += 20)
            {
                distinctColors.Add(output.GetPixel(x, y).ToArgb());
            }
        }

        if (distinctColors.Count < 8)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        output.Save(outputPath, ImageFormat.Png);
        return true;
    }

    private static void CompositeMeasuredFrame(
        string clientPngPath,
        string iconPath,
        string outputPath,
        int clientW,
        int clientH,
        int clientLeft,
        int clientTop,
        bool dark)
    {
        DrawingColor titleBg = dark ? DrawingColor.FromArgb(0x20, 0x20, 0x20) : DrawingColor.FromArgb(0xF3, 0xF3, 0xF3);
        DrawingColor borderColor = dark ? DrawingColor.FromArgb(0x3D, 0x3D, 0x3D) : DrawingColor.FromArgb(0xD1, 0xD1, 0xD1);
        DrawingColor titleText = dark ? DrawingColor.FromArgb(0xFF, 0xFF, 0xFF) : DrawingColor.FromArgb(0x1A, 0x1A, 0x1A);
        DrawingColor glyphColor = dark ? DrawingColor.FromArgb(0xE0, 0xE0, 0xE0) : DrawingColor.FromArgb(0x1A, 0x1A, 0x1A);

        using var canvas = new Bitmap(OuterWidth, OuterHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(titleBg);

        using (var client = new Bitmap(clientPngPath))
        {
            g.DrawImage(client, new Rectangle(clientLeft, clientTop, clientW, clientH));
        }

        int titleBarH = clientTop;
        int iconY = Math.Max(4, (titleBarH - 16) / 2);
        using (var icon = new Icon(iconPath, new System.Drawing.Size(16, 16)))
        {
            g.DrawIcon(icon, new Rectangle(12, iconY, 16, 16));
        }

        using (var font = new Font("Segoe UI", 9.0f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point))
        using (var textBrush = new SolidBrush(titleText))
        {
            g.DrawString("Berth", font, textBrush, new PointF(36, iconY));
        }

        int midY = titleBarH / 2;
        using (var glyphPen = new Pen(glyphColor, 1.0f))
        {
            int minX = OuterWidth - 115;
            g.DrawLine(glyphPen, minX - 5, midY, minX + 5, midY);

            int maxX = OuterWidth - 69;
            g.DrawRectangle(glyphPen, maxX - 5, midY - 5, 10, 10);

            int closeX = OuterWidth - 23;
            g.DrawLine(glyphPen, closeX - 5, midY - 5, closeX + 5, midY + 5);
            g.DrawLine(glyphPen, closeX + 5, midY - 5, closeX - 5, midY + 5);
        }

        using (var borderPen = new Pen(borderColor, 1.0f))
        {
            g.DrawRectangle(borderPen, 0, 0, OuterWidth - 1, OuterHeight - 1);
            g.DrawLine(borderPen, 1, clientTop - 1, OuterWidth - 2, clientTop - 1);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        canvas.Save(outputPath, ImageFormat.Png);
    }

    private static string ResolveRepoFile(params string[] parts)
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Berth.slnx")))
            {
                return Path.Combine([dir, .. parts]);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Berth.slnx above " + AppContext.BaseDirectory);
    }

    private static IconPosition[] BuildSampleIcons(int total, int monitor2Count = 12)
    {
        int mon1Count = total - monitor2Count;
        var icons = new List<IconPosition>(total);
        for (int i = 0; i < mon1Count; i++)
        {
            int col = i / 6;
            int row = i % 6;
            icons.Add(new IconPosition($"m1-{i}", $"App {i + 1}", 120 + (col * 220), 120 + (row * 190)));
        }

        for (int i = 0; i < monitor2Count; i++)
        {
            int col = i / 4;
            int row = i % 4;
            icons.Add(new IconPosition($"m2-{i}", $"Doc {i + 1}", 2680 + (col * 240), 120 + (row * 210)));
        }

        return [.. icons];
    }

    private static string ResolveOutputDirectory(string fallbackTempDir)
    {
        string? envDir = Environment.GetEnvironmentVariable("BERTH_DOCS_IMAGE_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            Directory.CreateDirectory(envDir);
            return envDir;
        }

        // Default to the test's own temporary folder so running dotnet test never overwrites tracked docs/images/.
        string outDir = Path.Combine(fallbackTempDir, "docs-images");
        Directory.CreateDirectory(outDir);
        return outDir;
    }

    private static byte[] AssertValid980x640Png(string path)
    {
        Assert.True(File.Exists(path), $"Expected screenshot at {path}");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1024, $"Screenshot at {path} is unexpectedly small ({bytes.Length} bytes).");
        using var ms = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Single(decoder.Frames);
        Assert.Equal(OuterWidth, decoder.Frames[0].PixelWidth);
        Assert.Equal(OuterHeight, decoder.Frames[0].PixelHeight);
        return bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
