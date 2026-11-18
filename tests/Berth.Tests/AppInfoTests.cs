using System.Text.RegularExpressions;
using System.Xml.Linq;
using Berth.Core;

namespace Berth.Tests;

public sealed class AppInfoTests
{
    [Fact]
    public void VersionIsThreeNumbers()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppInfo.Version);
    }

    [Fact]
    public void VersionIsTheOneInDirectoryBuildProps()
    {
        string props = Path.Combine(RepositoryRoot(), "Directory.Build.props");
        string expected = XDocument.Load(props).Descendants("Version").Single().Value;
        Assert.Equal(expected, AppInfo.Version);
    }

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
