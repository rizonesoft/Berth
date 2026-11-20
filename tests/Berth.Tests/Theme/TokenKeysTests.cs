using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSystemColors = System.Windows.SystemColors;

namespace Berth.Tests.Theme;

public sealed partial class TokenKeysTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SrcRoot = Path.Combine(RepoRoot, "src", "Berth");
    private static readonly string ThemeDir = Path.Combine(SrcRoot, "Theme");
    private static readonly string[] DictionaryFiles =
    [
        "Tokens.Light.xaml",
        "Tokens.Dark.xaml",
        "Tokens.HighContrast.xaml",
        "Tokens.Shared.xaml",
    ];

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey=)?(Berth\.[A-Za-z0-9_.]+)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupResourceRefRegex();

    [GeneratedRegex(@"ResourceKey=""(Berth\.[A-Za-z0-9_.]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex ElementResourceRefRegex();

    [Fact]
    public void GeneratedDictionariesLoadLooselyAndCompiledWithMatchingColorKeysAndDisjointSharedKeys()
    {
        RunOnSta(() =>
        {
            ResourceDictionary lightLoose = LoadLoose("Tokens.Light.xaml");
            ResourceDictionary darkLoose = LoadLoose("Tokens.Dark.xaml");
            ResourceDictionary hcLoose = LoadLoose("Tokens.HighContrast.xaml");
            ResourceDictionary sharedLoose = LoadLoose("Tokens.Shared.xaml");

            HashSet<string> lightKeys = KeysOf(lightLoose);
            HashSet<string> darkKeys = KeysOf(darkLoose);
            HashSet<string> hcKeys = KeysOf(hcLoose);
            HashSet<string> sharedKeys = KeysOf(sharedLoose);

            Assert.Equal(17, lightKeys.Count);
            Assert.Equal(lightKeys, darkKeys);
            Assert.Equal(lightKeys, hcKeys);
            Assert.Empty(sharedKeys.Intersect(lightKeys));

            // Compiled component URIs must also load and yield usable values.
            ResourceDictionary lightCompiled = LoadCompiled("Tokens.Light.xaml");
            ResourceDictionary darkCompiled = LoadCompiled("Tokens.Dark.xaml");
            ResourceDictionary hcCompiled = LoadCompiled("Tokens.HighContrast.xaml");
            ResourceDictionary sharedCompiled = LoadCompiled("Tokens.Shared.xaml");

            Assert.Equal(lightKeys, KeysOf(lightCompiled));
            Assert.Equal(darkKeys, KeysOf(darkCompiled));
            Assert.Equal(hcKeys, KeysOf(hcCompiled));
            Assert.Equal(sharedKeys, KeysOf(sharedCompiled));

            // Merging Shared + Light must return a SolidColorBrush for Berth.MonitorStroke and a Double (1.5) for Berth.MonitorStrokeThickness.
            var merged = new ResourceDictionary();
            merged.MergedDictionaries.Add(sharedCompiled);
            merged.MergedDictionaries.Add(lightCompiled);

            var strokeBrush = Assert.IsType<SolidColorBrush>(merged["Berth.MonitorStroke"]);
            Assert.NotNull(strokeBrush);
            var strokeThickness = Assert.IsType<double>(merged["Berth.MonitorStrokeThickness"]);
            Assert.Equal(1.5, strokeThickness);

            // Verify all three font family resources and all six TextBlock type styles.
            Assert.Equal("Segoe UI Variable Text, Segoe UI", Assert.IsType<WpfFontFamily>(sharedCompiled["Berth.Type.Family"]).Source);
            Assert.Equal("Segoe UI Variable Display, Segoe UI", Assert.IsType<WpfFontFamily>(sharedCompiled["Berth.Type.FamilyDisplay"]).Source);
            Assert.Equal("Cascadia Mono, Consolas", Assert.IsType<WpfFontFamily>(sharedCompiled["Berth.Type.FamilyMono"]).Source);

            (string StyleKey, string ExpectedFamily, double ExpectedSize, FontWeight ExpectedWeight)[] expectedStyles =
            [
                ("Berth.Type.Caption", "Segoe UI Variable Text, Segoe UI", 12, FontWeights.Normal),
                ("Berth.Type.Body", "Segoe UI Variable Text, Segoe UI", 14, FontWeights.Normal),
                ("Berth.Type.BodyStrong", "Segoe UI Variable Text, Segoe UI", 14, FontWeights.SemiBold),
                ("Berth.Type.Title", "Segoe UI Variable Display, Segoe UI", 24, FontWeights.SemiBold),
                ("Berth.Type.Mono", "Cascadia Mono, Consolas", 12, FontWeights.Normal),
                ("Berth.Type.MonoStrong", "Cascadia Mono, Consolas", 13, FontWeights.Normal),
            ];

            foreach ((string styleKey, string expectedFamily, double expectedSize, FontWeight expectedWeight) in expectedStyles)
            {
                var host = new Border();
                host.Resources.MergedDictionaries.Add(sharedCompiled);
                var tb = new TextBlock { Style = Assert.IsType<Style>(host.FindResource(styleKey)) };
                host.Child = tb;

                Assert.Equal(expectedFamily, tb.FontFamily.Source);
                Assert.Equal(expectedSize, tb.FontSize);
                Assert.Equal(expectedWeight, tb.FontWeight);
            }
        });
    }

    [Fact]
    public void HighContrastBrushesFollowSystemColorsDynamically()
    {
        RunOnSta(() =>
        {
            ResourceDictionary hc = LoadCompiled("Tokens.HighContrast.xaml");

            var border = new Border();
            border.Resources.MergedDictionaries.Add(hc);
            border.SetResourceReference(Border.BackgroundProperty, "Berth.MapBackground");
            border.SetResourceReference(Border.BorderBrushProperty, "Berth.MarkerSelected");

            border.Resources[WpfSystemColors.WindowColorKey] = Colors.Magenta;
            border.Resources[WpfSystemColors.HighlightColorKey] = Colors.Cyan;

            var bg = Assert.IsType<SolidColorBrush>(border.Background);
            var fg = Assert.IsType<SolidColorBrush>(border.BorderBrush);
            Assert.Equal(Colors.Magenta, bg.Color);
            Assert.Equal(Colors.Cyan, fg.Color);

            border.Resources[WpfSystemColors.WindowColorKey] = Colors.Lime;
            Assert.Equal(Colors.Lime, ((SolidColorBrush)border.Background).Color);
        });
    }

    [Fact]
    public void EveryBerthResourceKeyReferencedInSrcXamlExistsInGeneratedDictionaries()
    {
        HashSet<string> knownKeys = GetAllGeneratedKeys();
        List<string> missing = FindMissingKeys(SrcRoot, knownKeys);
        Assert.Empty(missing);
    }

    [Fact]
    public void MissingKeyScannerDetectsUnknownBerthKey()
    {
        HashSet<string> knownKeys = GetAllGeneratedKeys();
        string tempDir = Path.Combine(Path.GetTempPath(), "berth-token-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sample = Path.Combine(tempDir, "Sample.xaml");
            File.WriteAllText(
                sample,
                """
                <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Border Background="{DynamicResource Berth.NoSuchKey}"
                            BorderBrush="{StaticResource Berth.MarkerInPlace}" />
                </UserControl>
                """);

            List<string> missing = FindMissingKeys(tempDir, knownKeys);
            string sole = Assert.Single(missing);
            Assert.Contains("Berth.NoSuchKey", sole, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static HashSet<string> GetAllGeneratedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        RunOnSta(() =>
        {
            foreach (string name in DictionaryFiles)
            {
                keys.UnionWith(KeysOf(LoadLoose(name)));
            }
        });
        return keys;
    }

    internal static List<string> FindMissingKeys(string sourceRoot, IReadOnlySet<string> knownKeys)
    {
        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(sourceRoot, file))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (Match match in MarkupResourceRefRegex().Matches(text))
            {
                string key = match.Groups[1].Value;
                if (!knownKeys.Contains(key))
                {
                    missing.Add($"{Path.GetRelativePath(sourceRoot, file)}: {key}");
                }
            }

            foreach (Match match in ElementResourceRefRegex().Matches(text))
            {
                string key = match.Groups[1].Value;
                if (!knownKeys.Contains(key))
                {
                    missing.Add($"{Path.GetRelativePath(sourceRoot, file)}: {key}");
                }
            }
        }

        return missing;
    }

    private static ResourceDictionary LoadLoose(string fileName)
    {
        using FileStream stream = File.OpenRead(Path.Combine(ThemeDir, fileName));
        return Assert.IsType<ResourceDictionary>(XamlReader.Load(stream));
    }

    private static ResourceDictionary LoadCompiled(string fileName)
    {
        var uri = new Uri($"/Berth;component/Theme/{fileName}", UriKind.Relative);
        return Assert.IsType<ResourceDictionary>(WpfApplication.LoadComponent(uri));
    }

    private static HashSet<string> KeysOf(ResourceDictionary dict) =>
        dict.Keys.Cast<string>().ToHashSet(StringComparer.Ordinal);

    private static bool IsBuildOutput(string root, string file)
    {
        string rel = Path.GetRelativePath(root, file);
        string[] parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Berth.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate Berth.slnx above test output.");
    }
}
