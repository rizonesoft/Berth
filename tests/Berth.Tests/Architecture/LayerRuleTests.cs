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

    [Fact]
    public void ASourceInAnotherNamespaceIsCaught()
    {
        Assert.False(DeclaresCoreNamespace("namespace Berth.Elsewhere;\ninternal static class X { }"));
        Assert.False(DeclaresCoreNamespace("namespace Berth.CoreTools { }"));
        Assert.True(DeclaresCoreNamespace("namespace Berth.Core.Model;"));
        Assert.True(DeclaresCoreNamespace("namespace Berth.Core\n{\n}"));
    }

    /// <summary>True when every namespace the source declares is Berth.Core or below it, and it declares one.</summary>
    internal static bool DeclaresCoreNamespace(string source)
    {
        string[] names = NamespaceDeclaration().Matches(source).Select(m => m.Groups[1].Value).ToArray();
        return names.Length > 0 && names.All(n => n == "Berth.Core" || n.StartsWith("Berth.Core.", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

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
