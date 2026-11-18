using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class ArrangementKeyTests
{
    private static readonly MonitorInfo Left = new(-1920, -200, 1920, 1080, 100, false);
    private static readonly MonitorInfo Main = new(0, 0, 2560, 1440, 150, true);
    private static readonly MonitorInfo Below = new(0, 1440, 1920, 1080, 125, false);

    /// <summary>The three monitors' key, from the string written out by hand in SPEC §14's form.</summary>
    private const string ThreeMonitorKey = "2681c996402f364e";


    [Fact]
    public void OneMonitorHasItsKnownKey()
    {
        Assert.Equal("c7f017905ce518e6", ArrangementKey.From([new MonitorInfo(0, 0, 2560, 1440, 100, true)]));
    }

    [Fact]
    public void TheExpectedKeysAreTheShaOfTheHandWrittenStrings()
    {
        Assert.Equal("c7f017905ce518e6", Sha16("0,0,2560,1440,100"));
        Assert.Equal(ThreeMonitorKey, Sha16("-1920,-200,1920,1080,100;0,0,2560,1440,150;0,1440,1920,1080,125"));
    }

    [Theory]
    [InlineData("012")]
    [InlineData("021")]
    [InlineData("102")]
    [InlineData("120")]
    [InlineData("201")]
    [InlineData("210")]
    public void ThreeMonitorsInAnyOrderHaveOneKey(string order)
    {
        MonitorInfo[] all = [Left, Main, Below];
        Assert.Equal(ThreeMonitorKey, ArrangementKey.From(order.Select(c => all[c - '0']).ToArray()));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    [InlineData("Width")]
    [InlineData("Height")]
    [InlineData("Scale")]
    public void ChangingOneValueByOneChangesTheKey(string field)
    {
        MonitorInfo changed = field switch
        {
            "X" => Main with { X = Main.X + 1 },
            "Y" => Main with { Y = Main.Y + 1 },
            "Width" => Main with { Width = Main.Width + 1 },
            "Height" => Main with { Height = Main.Height + 1 },
            _ => Main with { Scale = Main.Scale + 1 },
        };

        Assert.NotEqual(ThreeMonitorKey, ArrangementKey.From([Left, changed, Below]));
    }

    [Fact]
    public void PrimaryDoesNotChangeTheKey()
    {
        Assert.Equal(ThreeMonitorKey, ArrangementKey.From([Left with { Primary = true }, Main with { Primary = false }, Below]));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    public void TheCultureDoesNotChangeTheKey(string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(ThreeMonitorKey, ArrangementKey.From([Left, Main, Below]));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void SwedishReallyUsesAnotherNegativeSign()
    {
        Assert.NotEqual("-", new CultureInfo("sv-SE").NumberFormat.NegativeSign);
    }

    [Fact]
    public void AKeyIsSixteenLowercaseHexCharacters()
    {
        Assert.Matches("^[0-9a-f]{16}$", ArrangementKey.From([Left, Main, Below]));
    }

    [Fact]
    public void NoMonitorIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ArrangementKey.From([]));
    }

    [Fact]
    public void TheSpecExampleKeyIsItsMonitorsKey()
    {
        Assert.Equal("c7f017905ce518e6", ArrangementKey.From([new MonitorInfo(0, 0, 2560, 1440, 100, true)]));
        string spec = File.ReadAllText(Path.Combine(RepositoryRoot(), "design", "SPEC.md"));
        Assert.Contains("\"key\": \"c7f017905ce518e6\"", spec, StringComparison.Ordinal);
        Assert.Contains("{ \"x\": 0, \"y\": 0, \"width\": 2560, \"height\": 1440, \"scale\": 100, \"primary\": true }", spec, StringComparison.Ordinal);
    }

    private static string Sha16(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

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
