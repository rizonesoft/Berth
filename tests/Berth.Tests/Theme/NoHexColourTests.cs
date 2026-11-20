using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

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

    // Explicit 3-, 4-, 6-, and 8-hex-digit alternatives with boundaries so 5- and 7-digit runs are not matched.
    [GeneratedRegex(@"(?<![0-9A-Fa-f#])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})(?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex HexColourRegex();

    [GeneratedRegex(@"\bColor\s*\.\s*From(?:Argb|Rgb|ScRgb)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ColorFromCallRegex();

    [Fact]
    public void SourceTreeContainsNoHexColoursOrColorFromRgbCalls()
    {
        List<string> violations = FindViolations(SrcRoot);
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("#ABC", true)]
    [InlineData("#ABCD", true)]
    [InlineData("#AABBCC", true)]
    [InlineData("#AABBCCDD", true)]
    [InlineData("#12345", false)]
    [InlineData("#1234567", false)]
    [InlineData("#12", false)]
    [InlineData("#123456789", false)]
    public void HexMatcherAcceptsExactlyThreeFourSixAndEightDigitLiterals(string literal, bool expectedMatch)
    {
        bool matched = HexColourRegex().IsMatch(literal);
        Assert.Equal(expectedMatch, matched);
    }

    [Fact]
    public void DiscoveryPathFlagsXamlHexMultilineAndCSharpFromRgbFromArgbWhileIgnoringCommentsAndExemptFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "berth-no-hex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Theme"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Window"));
        try
        {
            // Exempt generated token file should not trigger a violation.
            File.WriteAllText(
                Path.Combine(tempDir, "Theme", "Tokens.Light.xaml"),
                """<SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Color="#FFFFFF" />""");

            // Non-generated file inside Theme/ must still be scanned.
            File.WriteAllText(
                Path.Combine(tempDir, "Theme", "Extra.xaml"),
                """<Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Background="#00FF00" />""");

            // Multiline XAML attribute value must be detected.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "MultilineBad.xaml"),
                "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"\n#FF0000\" />\n");

            // Commented-out XAML must not trigger a violation.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "Commented.xaml"),
                """
                <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <!-- <Border Background="#FF0000" /> -->
                </Border>
                """);

            // Bad C# Color.FromRgb and Color.FromArgb calls.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "BadRgb.cs"),
                """
                using System.Windows.Media;
                class BadRgb { static Color C => Color.FromRgb(1, 2, 3); }
                """);

            File.WriteAllText(
                Path.Combine(tempDir, "Window", "BadArgb.cs"),
                """
                using System.Windows.Media;
                class BadArgb { static Color C => Color
                    .FromArgb(255, 1, 2, 3); }
                """);

            // Bad C# call inside string interpolation must also be detected.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "BadInterpolated.cs"),
                """
                using System.Windows.Media;
                class BadInterpolated { static string S => $"c={Color.FromRgb(1, 2, 3)}"; }
                """);

            // C# comments mentioning Color.FromRgb or hex literals must not trigger a violation.
            File.WriteAllText(
                Path.Combine(tempDir, "Window", "CommentOnly.cs"),
                """
                // Never call Color.FromRgb(1, 2, 3) or Color.FromArgb(255, 1, 2, 3) or use "#FF0000".
                /* Color.FromScRgb(1, 1, 1, 1) "#AABBCC" */
                class CommentOnly { }
                """);

            List<string> violations = FindViolations(tempDir);
            Assert.Equal(5, violations.Count);
            Assert.Contains(violations, v => v.Contains("Extra.xaml", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("MultilineBad.xaml", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("BadRgb.cs", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("BadArgb.cs", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("BadInterpolated.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(violations, v => v.Contains("Tokens.Light.xaml", StringComparison.Ordinal));
            Assert.DoesNotContain(violations, v => v.Contains("Commented.xaml", StringComparison.Ordinal));
            Assert.DoesNotContain(violations, v => v.Contains("CommentOnly.cs", StringComparison.Ordinal));
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

            string text = File.ReadAllText(file);
            if (ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                ScanXaml(rel, text, violations);
            }
            else
            {
                ScanCSharp(rel, text, violations);
            }
        }

        return violations;
    }

    private static void ScanXaml(string rel, string text, List<string> violations)
    {
        XDocument doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
        foreach (XElement element in doc.Descendants())
        {
            foreach (XAttribute attr in element.Attributes())
            {
                Match hex = HexColourRegex().Match(attr.Value);
                if (hex.Success)
                {
                    int line = ((IXmlLineInfo)attr).HasLineInfo() ? ((IXmlLineInfo)attr).LineNumber : 0;
                    violations.Add($"{rel}:{line}: hex colour literal {hex.Value}");
                }
            }
        }
    }

    private static void ScanCSharp(string rel, string text, List<string> violations)
    {
        // Lex C# into non-comment code (for Color.From* calls) and string literals (for hex colour literals),
        // skipping // and /* */ comments while tracking line numbers.
        var codeBuilder = new StringBuilder(text.Length);
        int line = 1;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                line++;
                codeBuilder.Append('\n');
                i++;
                continue;
            }

            // Line comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i += 2;
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // Block comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        codeBuilder.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(i + 2, text.Length);
                continue;
            }

            // Interpolated string $"...", $@"...", @$"..."
            if ((c == '$' && i + 1 < text.Length && text[i + 1] == '"')
                || (c == '$' && i + 2 < text.Length && text[i + 1] == '@' && text[i + 2] == '"')
                || (c == '@' && i + 2 < text.Length && text[i + 1] == '$' && text[i + 2] == '"'))
            {
                bool isVerbatim = (c == '@' || text[i + 1] == '@');
                int startLine = line;
                i += isVerbatim ? 3 : 2;
                var literalChunk = new StringBuilder();
                while (i < text.Length)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        codeBuilder.Append('\n');
                        literalChunk.Append('\n');
                        i++;
                        continue;
                    }

                    if (!isVerbatim && text[i] == '\\' && i + 1 < text.Length)
                    {
                        literalChunk.Append(text[i]);
                        literalChunk.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (isVerbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        literalChunk.Append('"');
                        i += 2;
                        continue;
                    }

                    if (text[i] == '"')
                    {
                        i++;
                        break;
                    }

                    if (text[i] == '{')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '{')
                        {
                            literalChunk.Append('{');
                            i += 2;
                            continue;
                        }

                        // Interpolation expression hole: emit its C# code into codeBuilder!
                        CheckLiteralForHex(rel, startLine, literalChunk.ToString(), violations);
                        literalChunk.Clear();
                        i++;
                        int depth = 1;
                        codeBuilder.Append(' ');
                        while (i < text.Length && depth > 0)
                        {
                            if (text[i] == '\n')
                            {
                                line++;
                            }

                            if (text[i] == '{')
                            {
                                depth++;
                            }
                            else if (text[i] == '}')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    i++;
                                    break;
                                }
                            }

                            codeBuilder.Append(text[i]);
                            i++;
                        }

                        codeBuilder.Append(' ');
                        startLine = line;
                        continue;
                    }

                    literalChunk.Append(text[i]);
                    i++;
                }

                CheckLiteralForHex(rel, startLine, literalChunk.ToString(), violations);
                continue;
            }

            // Verbatim string @"..."
            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                int startLine = line;
                i += 2;
                int start = i;
                while (i < text.Length)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                    }
                    else if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    i++;
                }

                CheckLiteralForHex(rel, startLine, text[start..i], violations);
                codeBuilder.Append("\"\"");
                i = Math.Min(i + 1, text.Length);
                continue;
            }

            // Raw string """...""" or regular string "..."
            if (c == '"')
            {
                int startLine = line;
                int quoteCount = 0;
                while (i + quoteCount < text.Length && text[i + quoteCount] == '"')
                {
                    quoteCount++;
                }

                if (quoteCount >= 3)
                {
                    i += quoteCount;
                    int start = i;
                    string closing = new('"', quoteCount);
                    int end = text.IndexOf(closing, i, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        end = text.Length;
                    }

                    string content = text[start..end];
                    foreach (char ch in content)
                    {
                        if (ch == '\n')
                        {
                            line++;
                        }
                    }

                    CheckLiteralForHex(rel, startLine, content, violations);
                    codeBuilder.Append("\"\"");
                    i = Math.Min(end + quoteCount, text.Length);
                    continue;
                }

                i++;
                int regularStart = i;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (text[i] == '\n')
                    {
                        line++;
                    }

                    i++;
                }

                CheckLiteralForHex(rel, startLine, text[regularStart..i], violations);
                codeBuilder.Append("\"\"");
                i = Math.Min(i + 1, text.Length);
                continue;
            }

            // Char literal '...'
            if (c == '\'')
            {
                i++;
                while (i < text.Length && text[i] != '\'')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    i++;
                }

                codeBuilder.Append("''");
                i = Math.Min(i + 1, text.Length);
                continue;
            }

            codeBuilder.Append(c);
            i++;
        }

        string strippedCode = codeBuilder.ToString();
        foreach (Match match in ColorFromCallRegex().Matches(strippedCode))
        {
            int callLine = 1 + strippedCode.AsSpan(0, match.Index).Count('\n');
            violations.Add($"{rel}:{callLine}: Color.From* call");
        }
    }

    private static void CheckLiteralForHex(string rel, int line, string literalContent, List<string> violations)
    {
        Match hex = HexColourRegex().Match(literalContent);
        if (hex.Success)
        {
            violations.Add($"{rel}:{line}: hex colour literal {hex.Value}");
        }
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
