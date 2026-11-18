using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Berth.Tests.Architecture;

public sealed class LayerRuleTests
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
    [InlineData("Berth.Tests.LayerFixtures.Core.ComImportUser.com uses Berth.Tests.LayerFixtures.Outside.IComImported from Berth.Tests")]
    [InlineData("Berth.Tests.LayerFixtures.Core.GeneratedComUser.com uses Berth.Tests.LayerFixtures.Outside.IComGenerated from Berth.Tests")]
    [InlineData("Berth.Tests.LayerFixtures.Core.OuterLayerUser.outer uses Berth.Tests.LayerFixtures.Outside.OuterLayer from Berth.Tests")]
    [InlineData("Berth.Tests.LayerFixtures.Core.CatchOnly.Run uses System.Windows.Markup.XamlParseException from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.AttributeValue uses System.Windows.Window from PresentationFramework")]
    [InlineData("Berth.Tests.LayerFixtures.Core.NamedAttributeValue uses System.Windows.Forms.Form from System.Windows.Forms")]
    [InlineData("Berth.Tests.LayerFixtures.Core.GenericAttributeValue uses System.Windows.Window from PresentationFramework")]
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
        Assert.DoesNotContain(
            LayerRule.Check(TestAssembly, Fixtures),
            line => line.StartsWith(Fixtures + ".CleanGenericAttributeValue", StringComparison.Ordinal));
        Assert.DoesNotContain(
            LayerRule.Check(TestAssembly, Fixtures),
            line => line.StartsWith(Fixtures + ".CleanGenerated", StringComparison.Ordinal) || line.Contains("RegexGenerator", StringComparison.Ordinal));
    }

    [Fact]
    public void CoreSourcesDeclareTheCoreNamespace()
    {
        string core = Path.Combine(RepositoryRoot(), "src", "Berth", "Core");
        string[] files = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        string[] strays = files.Where(file => !DeclaresCoreNamespace(File.ReadAllText(file)))
            .Select(file => $"{file}: {string.Join(", ", StrayTypes(File.ReadAllText(file)))}")
            .ToArray();
        Assert.True(strays.Length == 0, "Files under src/Berth/Core that are not one file-scoped Berth.Core namespace:\n" + string.Join("\n", strays));
    }

    [Theory]
    [InlineData("namespace Berth.Core.Model;\n\ninternal sealed class X { }", true)]
    [InlineData("namespace Berth.Core;", true)]
    [InlineData("namespace Berth.Core;\n\ninternal static class X\n{\n    internal const string Doc = \"\"\"\n        namespace Berth.Elsewhere;\n        \"\"\";\n}", true)]
    [InlineData("namespace Berth.Core;\n// namespace Berth.Elsewhere;\ninternal static class X { }", true)]
    [InlineData("namespace Berth.Elsewhere;\ninternal static class X { }", false)]
    [InlineData("namespace Berth.CoreTools;\ninternal static class X { }", false)]
    [InlineData("namespace Berth.Core { }\ninternal class Stray { }", false)]
    [InlineData("namespace Berth.Core\n{\n    internal sealed class X { }\n}", false)]
    [InlineData("namespace Berth.Core;\nnamespace Berth.Core.Second;", false)]
    [InlineData("// namespace Berth.Core;\ninternal class Global { }", false)]
    [InlineData("/* namespace Berth.Core; */\ninternal enum Global { A }", false)]
    [InlineData("internal class NoNamespace { }", false)]
    [InlineData("", false)]
    [InlineData("#if !WINDOWS\nnamespace Berth.Core;\n#endif\ninternal class Hidden { }", false)]
    [InlineData("#define X\nnamespace Berth.Core;\ninternal class X1 { }", true)]
    [InlineData("namespace Berth.Core;\ninternal static class X\n{\n    internal static int Level()\n    {\n#if DEBUG\n        return 1;\n#else\n        return 2;\n#endif\n    }\n}", true)]
    [InlineData("#if WINDOWS\nnamespace Berth.Core;\n#else\nnamespace Berth.Elsewhere;\n#endif\ninternal class Chosen { }", true)]
    [InlineData("#nullable enable\nnamespace Berth.Core;\n#pragma warning disable CA1812\ninternal sealed class X { }", true)]
    public void ASourceFileMustBeOneFileScopedCoreNamespace(string source, bool accepted)
    {
        Assert.Equal(accepted, DeclaresCoreNamespace(source));
    }

    [Fact]
    public void TheProductionSymbolsAreThoseOfAWindowsBuild()
    {
        Assert.Contains("WINDOWS", ProductionSymbols);
        Assert.Contains("NET", ProductionSymbols);
        Assert.Contains(ProductionSymbols, s => s is "DEBUG" or "RELEASE");
    }

    [Fact]
    public void StrayTypesAreNamedWhereTheCompilerPutsThem()
    {
        Assert.Equal(["Stray"], StrayTypes("namespace Berth.Core { }\ninternal class Stray { }"));
        Assert.Equal(["Global"], StrayTypes("// namespace Berth.Core;\ninternal class Global { }"));
        Assert.Equal(["Berth.D"], StrayTypes("namespace Berth\n{\n    namespace Core\n    {\n        internal interface I { }\n    }\n\n    internal delegate void D();\n}"));
    }

    /// <summary>
    /// True when the C# parser finds exactly one namespace declaration in the source, file-scoped, named Berth.Core
    /// or below it, and no type outside it. Comments and strings never count, because the parser reads them as
    /// comments and strings.
    /// </summary>
    internal static bool DeclaresCoreNamespace(string source)
    {
        SyntaxNode root = Parse(source);
        BaseNamespaceDeclarationSyntax[] namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToArray();
        return namespaces is [FileScopedNamespaceDeclarationSyntax only]
            && InCore(only.Name.ToString())
            && StrayTypes(source).Count == 0;
    }

    /// <summary>The top-level types a source file declares outside Berth.Core, read with the C# parser.</summary>
    internal static IReadOnlyList<string> StrayTypes(string source)
    {
        SyntaxNode root = Parse(source);
        return root.DescendantNodes()
            .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Where(node => !node.Ancestors().Any(a => a is BaseTypeDeclarationSyntax))
            .Select(node => (Namespace: Namespace(node), Name: Name(node)))
            .Where(t => !InCore(t.Namespace))
            .Select(t => t.Namespace.Length == 0 ? t.Name : t.Namespace + "." + t.Name)
            .ToList();
    }

    /// <summary>The preprocessor symbols berth.exe is compiled with, embedded by the test project's build.</summary>
    internal static IReadOnlyList<string> ProductionSymbols { get; } = typeof(LayerRuleTests).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .Single(a => a.Key == "BerthDefineConstants").Value!
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Parses as the production compiler does, so #if decides the same structure it decides there.</summary>
    private static SyntaxNode Parse(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithPreprocessorSymbols(ProductionSymbols)).GetRoot();
    private static bool InCore(string name) => name == "Berth.Core" || name.StartsWith("Berth.Core.", StringComparison.Ordinal);
    private static string Namespace(SyntaxNode node) => string.Join(
        ".",
        node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));

    private static string Name(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.Text,
        _ => throw new ArgumentException("not a type declaration", nameof(node)),
    };
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
