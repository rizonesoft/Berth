using System.Text.RegularExpressions;

namespace Berth.Tests.Architecture;

public sealed partial class LayerRuleTests
{
    private const string Fixtures = "Berth.Tests.LayerFixtures.Core";

    private static string BerthAssembly => Path.Combine(AppContext.BaseDirectory, "berth.dll");

    private static string TestAssembly => typeof(LayerRuleTests).Assembly.Location;

    [Fact]
    public void CoreUsesOnlyCoreAndTheBaseRuntime()
    {
        IReadOnlyList<string> violations = LayerRule.Check(BerthAssembly, "Berth.Core");
        Assert.True(violations.Count == 0, "Berth.Core breaks the layer rule:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void CoreHasTypes()
    {
        IReadOnlyList<string> types = LayerRule.CoreTypes(BerthAssembly, "Berth.Core");
        LayerRule.RequireAny(types, "Berth.Core");
        Assert.Contains("Berth.Core.AppInfo", types);
    }

    [Fact]
    public void AnEmptySelectionIsRefused()
    {
        IReadOnlyList<string> none = LayerRule.CoreTypes(BerthAssembly, "Berth.Nothing");
        var error = Assert.Throws<InvalidOperationException>(() => LayerRule.RequireAny(none, "Berth.Nothing"));
        Assert.Equal("no type in Berth.Nothing", error.Message);
    }

    [Theory]
    [InlineData("Berth.Tests.LayerFixtures.Core.Bad.W uses System.Windows.Window from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.BodyOnly.Warn uses System.Windows.MessageBox from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.GenericArgument.windows uses System.Windows.Window from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.Ambient uses System.Windows.Markup.IQueryAmbient from System.Xaml")]
    [InlineData("Berth.Tests.LayerFixtures.Core.Primitives.Margin uses System.Windows.Forms.Padding from System.Windows.Forms.Primitives")]
    [InlineData("Berth.Tests.LayerFixtures.Core.ComImportUser.com uses the COM type Berth.Tests.LayerFixtures.Outside.IComImported from Berth.Tests")]
    [InlineData("Berth.Tests.LayerFixtures.Core.GeneratedComUser.com uses the COM type Berth.Tests.LayerFixtures.Outside.IComGenerated from Berth.Tests")]
    [InlineData("Berth.Tests.LayerFixtures.Core.OuterLayerUser.outer uses Berth.Tests.LayerFixtures.Outside.OuterLayer from Berth.Tests")]
    [InlineData("Berth.Tests.LayerFixtures.Core.CatchOnly.Run uses System.Windows.Markup.XamlParseException from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.AttributeValue uses System.Windows.Window from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.NamedAttributeValue uses System.Windows.Forms.Form from System.Windows.Forms")]
    [InlineData("Berth.Tests.LayerFixtures.Core.MarkedGeneratedUser.marked uses Berth.Tests.LayerFixtures.Outside.MarkedGenerated from Berth.Tests")]
    public void EachFixtureIsReported(string expected)
    {
        Assert.Contains(expected, LayerRule.Check(TestAssembly, Fixtures));
    }

    [Fact]
    public void TheCleanFixtureIsNotReported()
    {
        Assert.DoesNotContain(
            LayerRule.Check(TestAssembly, Fixtures),
            line => line.StartsWith(Fixtures + ".Clean", StringComparison.Ordinal));
    }

    [Fact]
    public void CoreSourcesDeclareTheCoreNamespace()
    {
        string core = Path.Combine(RepositoryRoot(), "src", "Berth", "Core");
        string[] files = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        string[] strays = files.Where(file => !DeclaresCoreNamespace(File.ReadAllText(file))).ToArray();
        Assert.True(strays.Length == 0, "Files under src/Berth/Core outside Berth.Core:\n" + string.Join("\n", strays));
    }

    [Theory]
    [InlineData("namespace Berth.Core.Model;\n\ninternal sealed class X { }", true)]
    [InlineData("namespace Berth.Core;", true)]
    [InlineData("namespace Berth.Elsewhere;\ninternal static class X { }", false)]
    [InlineData("namespace Berth.CoreTools;", false)]
    [InlineData("namespace Berth.Core { }\ninternal class Stray { }", false)]
    [InlineData("namespace Berth.Core\n{\n}", false)]
    [InlineData("namespace Berth.Core;\nnamespace Berth.Core.Second;", false)]
    [InlineData("internal class NoNamespace { }", false)]
    public void ASourceFileMustBeOneFileScopedCoreNamespace(string source, bool accepted)
    {
        Assert.Equal(accepted, DeclaresCoreNamespace(source));
    }

    /// <summary>
    /// True when the source declares exactly one namespace, file-scoped, that is Berth.Core or below it. C# allows
    /// no type before or outside a file-scoped namespace, so every type in such a file is in it; a block namespace
    /// is refused because a type may follow its closing brace.
    /// </summary>
    internal static bool DeclaresCoreNamespace(string source)
    {
        string[] fileScoped = FileScopedNamespace().Matches(source).Select(m => m.Groups[1].Value).ToArray();
        int all = AnyNamespace().Count(source);
        return fileScoped.Length == 1 && all == 1
            && (fileScoped[0] == "Berth.Core" || fileScoped[0].StartsWith("Berth.Core.", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();

    [GeneratedRegex(@"^\s*namespace\s+[\w.]+", RegexOptions.Multiline)]
    private static partial Regex AnyNamespace();
    private static string RepositoryRoot()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "Berth.slnx")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new InvalidOperationException("Berth.slnx was not found above the test output.");
    }
}
