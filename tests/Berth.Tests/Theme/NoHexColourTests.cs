using System.IO;
using System.Text.RegularExpressions;

namespace Berth.Tests.Theme;

public sealed partial class NoHexColourTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SrcRoot = Path.Combine(RepoRoot, "src", "Berth");

    private static readonly HashSet<string> ExemptFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        Path.Combine("Theme", "Tokens.Light.xaml"),
        Path.Combine("Theme", "Tokens.Dark.xaml"),
        Path.Combine("Theme", "Tokens.HighContrast.xaml"),
        Path.Combine("Theme", "Tokens.Shared.xaml"),
    };

    // Match double- or single-quoted string/attribute literals so we inspect their contents for hex colours.
    [GeneratedRegex("""(?:"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLiteralRegex();

    // Explicit 3-, 4-, 6-, and 8-hex-digit alternatives with boundaries so 5- and 7-digit runs are not matched.
    [GeneratedRegex(@"(?<![0-9A-Fa-f#])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})(?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex HexColourRegex();

    [GeneratedRegex(@"\bColor\.From(?:A?Rgb|ScRgb)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ColorFromCallRegex();

    [Fact]
    public void SourceTreeContainsNoHexColoursOrColorFromRgbCalls()
    {
        List<string> violations = FindViolations(SrcRoot);
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("\"#ABC\"", true)]
    [InlineData("\"#ABCD\"", true)]
    [InlineData("\"#AABBCC\"", true)]
    [InlineData("\"#AABBCCDD\"", true)]
    [InlineData("\"#12345\"", false)]
    [InlineData("\"#1234567\"", false)]
    [InlineData("\"#12\"", false)]
    [InlineData("\"#123456789\"", false)]
    public void HexMatcherAcceptsExactlyThreeFourSixAndEightDigitLiterals(string literal, bool expectedMatch)
    {
        bool matched = HexColourRegex().IsMatch(literal);
        Assert.Equal(expectedMatch, matched);
    }

    [Fact]
    public void DiscoveryPathFlagsXamlHexAndCSharpColorFromRgbWhileExemptingGeneratedTokenFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "berth-no-hex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Theme"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Window"));
        try
        {
            // Exempt generated token file should not trigger a violation.
            File.WriteAllText(
                Path.Combine(tempDir, "Theme", "Tokens.Light.xaml"),
                """<SolidColorBrush Color="#FFFFFF" />""");

            // Non-generated file inside Theme/ must still be scanned.
            File.WriteAllText(
                Path.Combine(tempDir, "Theme", "Extra.xaml"),
                """<Border Background="#00FF00" />""");

            // Bad XAML attribute in Window/.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "Bad.xaml"),
                """<Border Background="#FF0000" />""");

            // Bad C# Color.FromRgb call.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "Bad.cs"),
                """
                using System.Windows.Media;
                class Bad { static Color C => Color.FromRgb(1, 2, 3); }
                """);

            List<string> violations = FindViolations(tempDir);
            Assert.Equal(3, violations.Count);
            Assert.Contains(violations, v => v.Contains("Extra.xaml", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("Bad.xaml", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("Bad.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(violations, v => v.Contains("Tokens.Light.xaml", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    internal static List<string> FindViolations(string sourceRoot)
    {
        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (!ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rel = Path.GetRelativePath(sourceRoot, file);
            if (IsBuildOutput(rel) || ExemptFiles.Contains(rel))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (ColorFromCallRegex().IsMatch(line))
                {
                    violations.Add($"{rel}:{i + 1}: Color.From* call");
                }

                foreach (Match quoted in QuotedLiteralRegex().Matches(line))
                {
                    Match hex = HexColourRegex().Match(quoted.Value);
                    if (hex.Success)
                    {
                        violations.Add($"{rel}:{i + 1}: hex colour literal {hex.Value}");
                    }
                }
            }
        }

        return violations;
    }

    private static bool IsBuildOutput(string rel)
    {
        string[] parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("obj", StringComparison.OrdinalIgnoreCase));
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
