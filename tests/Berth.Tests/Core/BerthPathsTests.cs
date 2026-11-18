using Berth.Core;

namespace Berth.Tests.Core;

public sealed class BerthPathsTests
{
    private const string KeyA = "0123456789abcdef";
    private const string KeyB = "fedcba9876543210";

    private static void AssertPaths(BerthPaths paths, string data, string local)
    {
        Assert.Equal(data, paths.DataRoot);
        Assert.Equal(local, paths.LocalRoot);
        Assert.Equal(Path.Combine(data, "layouts"), paths.LayoutsFolder);
        Assert.Equal(Path.Combine(data, "layouts", KeyA + ".json"), paths.LayoutFile(KeyA));
        Assert.Equal(Path.Combine(data, "layouts", KeyB + ".json"), paths.LayoutFile(KeyB));
        Assert.Equal(Path.Combine(data, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(local, "undo.json"), paths.UndoFile);
        Assert.Equal(Path.Combine(local, "logs"), paths.LogFolder);
        Assert.Equal(Path.Combine(local, "logs", "berth.log"), paths.LogFile);
    }

    [Fact]
    public void ForRootPutsBothRootsUnderIt()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);

        AssertPaths(paths, Path.Combine(data.Root, "roaming"), Path.Combine(data.Root, "local"));
        Assert.False(paths.TestMode);
    }

    [Fact]
    public void TheOverrideRedirectsEveryPathAndTurnsTestModeOn()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.FromEnvironment(name => name == "BERTH_DATA" ? data.Root : null);

        AssertPaths(paths, Path.Combine(data.Root, "roaming"), Path.Combine(data.Root, "local"));
        Assert.True(paths.TestMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutAnOverrideThePathsAreTheUsersOwnAndTestModeIsOff(string? value)
    {
        BerthPaths paths = BerthPaths.FromEnvironment(name => name == "BERTH_DATA" ? value : null);

        // Built from the special folders and compared, never written.
        string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berth");
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berth");
        AssertPaths(paths, roaming, local);
        Assert.False(paths.TestMode);
    }

    [Fact]
    public void ARelativeOverrideIsMadeAbsolute()
    {
        BerthPaths paths = BerthPaths.FromEnvironment(name => name == "BERTH_DATA" ? "relative-root" : null);
        string root = Path.GetFullPath("relative-root");

        AssertPaths(paths, Path.Combine(root, "roaming"), Path.Combine(root, "local"));
        Assert.True(paths.TestMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    public void AnUnresolvedAppDataFolderIsRefusedNotMadeRelative(string given)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BerthPaths.FromEnvironment(
            _ => null,
            special => special == Environment.SpecialFolder.ApplicationData ? given : @"C:\Users\x\AppData\Local"));
        Assert.Contains("ApplicationData", error.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => BerthPaths.FromEnvironment(
            _ => null,
            special => special == Environment.SpecialFolder.LocalApplicationData ? given : @"C:\Users\x\AppData\Roaming"));
    }

    [Fact]
    public void TheDefaultRootsComeFromTheGivenFolders()
    {
        BerthPaths paths = BerthPaths.FromEnvironment(
            _ => null,
            special => special == Environment.SpecialFolder.ApplicationData ? @"C:\Users\x\AppData\Roaming" : @"C:\Users\x\AppData\Local");

        AssertPaths(paths, @"C:\Users\x\AppData\Roaming\Berth", @"C:\Users\x\AppData\Local\Berth");
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcdeg")]
    [InlineData("")]
    [InlineData("0123456789abcdef\n")]
    [InlineData("0123456789abcdef\r\n")]
    public void AKeyThatIsNotSixteenLowercaseHexIsRefused(string key)
    {
        using var data = new TestData();
        Assert.Throws<ArgumentException>(() => BerthPaths.ForRoot(data.Root).LayoutFile(key));
    }

    [Fact]
    public void EnsureFolderForCreatesOnlyThatFilesFolderAndCanRunTwice()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);

        BerthPaths.EnsureFolderFor(paths.LayoutFile(KeyA));
        BerthPaths.EnsureFolderFor(paths.LayoutFile(KeyA));

        Assert.True(Directory.Exists(paths.LayoutsFolder));
        Assert.False(Directory.Exists(paths.LocalRoot));
    }

    [Fact]
    public void ABrokenLogFolderDoesNotStopTheLayoutsFolder()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.LocalRoot);
        File.WriteAllText(paths.LogFolder, "a file where the log folder should be");

        BerthPaths.EnsureFolderFor(paths.LayoutFile(KeyA));
        Assert.True(Directory.Exists(paths.LayoutsFolder));
        Assert.ThrowsAny<IOException>(() => BerthPaths.EnsureFolderFor(paths.LogFile));
    }
}
