using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSystemColors = System.Windows.SystemColors;

namespace Berth.Tests.Theme;

public sealed partial class TokenKeysTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SrcRoot = Path.Combine(RepoRoot, "src", "Berth");
    private static readonly string ThemeDir = Path.Combine(SrcRoot, "Theme");
    private static readonly string TokensJsonPath = Path.Combine(RepoRoot, "design", "tokens.json");
    private static readonly string[] DictionaryFiles =
    [
        "Tokens.Light.xaml",
        "Tokens.Dark.xaml",
        "Tokens.HighContrast.xaml",
        "Tokens.Shared.xaml",
    ];

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey=)?(Berth\.[A-Za-z0-9_.]+)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupResourceRefRegex();

    [Fact]
    public void GeneratedDictionariesLoadLooselyAndCompiledWithEveryValueVerified()
    {
        using JsonDocument tokensDoc = JsonDocument.Parse(File.ReadAllText(TokensJsonPath));
        JsonElement root = tokensDoc.RootElement;

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

            // Compiled component URIs must also load and yield usable values for EVERY token.
            ResourceDictionary lightCompiled = LoadCompiled("Tokens.Light.xaml");
            ResourceDictionary darkCompiled = LoadCompiled("Tokens.Dark.xaml");
            ResourceDictionary hcCompiled = LoadCompiled("Tokens.HighContrast.xaml");
            ResourceDictionary sharedCompiled = LoadCompiled("Tokens.Shared.xaml");

            Assert.Equal(lightKeys, KeysOf(lightCompiled));
            Assert.Equal(darkKeys, KeysOf(darkCompiled));
            Assert.Equal(hcKeys, KeysOf(hcCompiled));
            Assert.Equal(sharedKeys, KeysOf(sharedCompiled));

            // Verify every compiled Light, Dark, and HighContrast brush value.
            foreach (JsonProperty prop in root.GetProperty("color").GetProperty("light").EnumerateObject())
            {
                string key = "Berth." + Pascal(prop.Name);
                var expected = (WpfColor)WpfColorConverter.ConvertFromString(prop.Value.GetString()!)!;
                var brush = Assert.IsType<SolidColorBrush>(lightCompiled[key]);
                Assert.Equal(expected, brush.Color);
                Assert.IsType<SolidColorBrush>(hcCompiled[key]);
            }

            foreach (JsonProperty prop in root.GetProperty("color").GetProperty("dark").EnumerateObject())
            {
                string key = "Berth." + Pascal(prop.Name);
                var expected = (WpfColor)WpfColorConverter.ConvertFromString(prop.Value.GetString()!)!;
                var brush = Assert.IsType<SolidColorBrush>(darkCompiled[key]);
                Assert.Equal(expected, brush.Color);
            }

            // Verify every compiled Shared size, radius, and timing value.
            foreach (JsonProperty prop in root.GetProperty("size").EnumerateObject())
            {
                string key = "Berth." + Pascal(prop.Name);
                Assert.Equal(prop.Value.GetDouble(), Assert.IsType<double>(sharedCompiled[key]));
            }

            foreach (JsonProperty prop in root.GetProperty("radius").EnumerateObject())
            {
                string key = "Berth.Radius" + Pascal(prop.Name);
                var radius = Assert.IsType<CornerRadius>(sharedCompiled[key]);
                double expected = prop.Value.GetDouble();
                Assert.Equal(new CornerRadius(expected), radius);
            }

            foreach (JsonProperty prop in root.GetProperty("timing").EnumerateObject())
            {
                string key = "Berth." + Pascal(prop.Name);
                Assert.Equal(prop.Value.GetDouble(), Assert.IsType<double>(sharedCompiled[key]));
            }

            // Merging Shared + Light must return a SolidColorBrush for Berth.MonitorStroke and a Double (1.5) for Berth.MonitorStrokeThickness.
            var merged = new ResourceDictionary();
            merged.MergedDictionaries.Add(sharedCompiled);
            merged.MergedDictionaries.Add(lightCompiled);

            Assert.IsType<SolidColorBrush>(merged["Berth.MonitorStroke"]);
            Assert.Equal(1.5, Assert.IsType<double>(merged["Berth.MonitorStrokeThickness"]));

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
    public void MissingKeyScannerDetectsMarkupAndSingleQuotedElementsWhileIgnoringXmlComments()
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
                    <!-- <Border Background="{DynamicResource Berth.CommentedOutKey}" /> -->
                    <Border Background="{DynamicResource Berth.NoSuchKey}"
                            BorderBrush="{StaticResource Berth.MarkerInPlace}">
                        <Border.Tag>
                            <DynamicResource ResourceKey='Berth.SingleQuotedMissing' />
                        </Border.Tag>
                    </Border>
                </UserControl>
                """);

            List<string> missing = FindMissingKeys(tempDir, knownKeys);
            Assert.Equal(2, missing.Count);
            Assert.Contains(missing, m => m.Contains("Berth.NoSuchKey", StringComparison.Ordinal));
            Assert.Contains(missing, m => m.Contains("Berth.SingleQuotedMissing", StringComparison.Ordinal));
            Assert.DoesNotContain(missing, m => m.Contains("Berth.CommentedOutKey", StringComparison.Ordinal));
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

            string rel = Path.GetRelativePath(sourceRoot, file);
            XDocument doc = XDocument.Parse(File.ReadAllText(file), LoadOptions.SetLineInfo);
            foreach (XElement element in doc.Descendants())
            {
                if (element.Name.LocalName is "StaticResource" or "DynamicResource")
                {
                    XAttribute? keyAttr = element.Attribute("ResourceKey");
                    if (keyAttr is not null && keyAttr.Value.StartsWith("Berth.", StringComparison.Ordinal) && !knownKeys.Contains(keyAttr.Value))
                    {
                        int line = ((IXmlLineInfo)keyAttr).HasLineInfo() ? ((IXmlLineInfo)keyAttr).LineNumber : 0;
                        missing.Add($"{rel}:{line}: {keyAttr.Value}");
                    }
                }

                foreach (XAttribute attr in element.Attributes())
                {
                    foreach (Match match in MarkupResourceRefRegex().Matches(attr.Value))
                    {
                        string key = match.Groups[1].Value;
                        if (!knownKeys.Contains(key))
                        {
                            int line = ((IXmlLineInfo)attr).HasLineInfo() ? ((IXmlLineInfo)attr).LineNumber : 0;
                            missing.Add($"{rel}:{line}: {key}");
                        }
                    }
                }
            }
        }

        return missing;
    }

    private static string Pascal(string name) =>
        char.ToUpperInvariant(name[0]) + name[1..];

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
