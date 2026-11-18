using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class LayoutJsonTests
{
    /// <summary>The example of design/SPEC.md §14, verbatim.</summary>
    private const string SpecExample = """
        {
          "schemaVersion": 1,
          "key": "3f9a61c2d0b47e15",
          "name": "Docked",
          "savedAt": "2026-11-15T07:14:03Z",
          "monitors": [
            { "x": 0, "y": 0, "width": 2560, "height": 1440, "scale": 100, "primary": true }
          ],
          "icons": [
            { "id": "::{645FF040-5081-101B-9F08-00AA002F954E}", "name": "Recycle Bin", "x": 76, "y": 2 }
          ],
          "previous": null
        }
        """;

    private const string WithPrevious = """
        {
          "schemaVersion": 1,
          "key": "0123456789abcdef",
          "name": "2 monitors",
          "savedAt": "2026-11-16T10:00:00Z",
          "monitors": [
            { "x": -1920, "y": 0, "width": 1920, "height": 1080, "scale": 100, "primary": false },
            { "x": 0, "y": 0, "width": 2560, "height": 1440, "scale": 150, "primary": true }
          ],
          "icons": [
            { "id": "C:\\Users\\a\\Desktop\\Notes.txt", "name": "Notes.txt", "x": 1436, "y": 702 },
            { "id": "C:\\Users\\a\\Desktop\\Zero.txt", "name": "Zero.txt", "x": 0, "y": 0 }
          ],
          "previous": {
            "savedAt": "2026-11-15T07:14:03Z",
            "icons": [
              { "id": "C:\\Users\\a\\Desktop\\Notes.txt", "name": "Notes.txt", "x": -1800, "y": 40 }
            ]
          }
        }
        """;

    private static Layout Parse(string json) => BerthJson.Parse(System.Text.Encoding.UTF8.GetBytes(json), BerthJson.Default.Layout);

    private static string Write(Layout layout) => JsonSerializer.Serialize(layout, BerthJson.Default.Layout);

    [Fact]
    public void SpecExampleParsesToItsValues()
    {
        Layout layout = Parse(SpecExample);

        Assert.Equal(1, layout.SchemaVersion);
        Assert.Equal("3f9a61c2d0b47e15", layout.Key);
        Assert.Equal("Docked", layout.Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), layout.SavedAt);
        Assert.Equal([new MonitorInfo(0, 0, 2560, 1440, 100, true)], layout.Monitors);
        Assert.Equal([new IconPosition("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 76, 2)], layout.Icons);
        Assert.Null(layout.Previous);
    }

    [Fact]
    public void PreviousSaveParsesToItsValues()
    {
        Layout layout = Parse(WithPrevious);

        Assert.Equal(
            [new MonitorInfo(-1920, 0, 1920, 1080, 100, false), new MonitorInfo(0, 0, 2560, 1440, 150, true)],
            layout.Monitors);
        Assert.Equal(
            [
                new IconPosition(@"C:\Users\a\Desktop\Notes.txt", "Notes.txt", 1436, 702),
                new IconPosition(@"C:\Users\a\Desktop\Zero.txt", "Zero.txt", 0, 0),
            ],
            layout.Icons);
        Assert.NotNull(layout.Previous);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), layout.Previous.SavedAt);
        Assert.Equal([new IconPosition(@"C:\Users\a\Desktop\Notes.txt", "Notes.txt", -1800, 40)], layout.Previous.Icons);
    }

    [Theory]
    [InlineData(SpecExample)]
    [InlineData(WithPrevious)]
    public void WritingGivesTheExpectedDocument(string expected)
    {
        string written = Write(Parse(expected));

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(written)),
            "The written document differs from the expected one:\n" + written);
        Assert.Contains("\n  \"schemaVersion\": 1,", written.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TimesAreWrittenAsUtcEndingInZ()
    {
        var local = new DateTimeOffset(2026, 11, 15, 9, 14, 3, 500, TimeSpan.FromHours(2));
        var earlier = new DateTimeOffset(2026, 11, 14, 23, 30, 0, TimeSpan.FromHours(-5));
        var layout = new Layout(
            1, "0123456789abcdef", "x", local, [new MonitorInfo(0, 0, 10, 10, 100, true)], [], new PreviousSave(earlier, []));

        JsonNode written = JsonNode.Parse(Write(layout))!;

        Assert.Equal("2026-11-15T07:14:03Z", (string?)written["savedAt"]);
        Assert.Equal("2026-11-15T04:30:00Z", (string?)written["previous"]!["savedAt"]);
    }

    [Fact]
    public void TimesWithAnOffsetAreReadAsUtc()
    {
        Layout layout = Parse(SpecExample.Replace("2026-11-15T07:14:03Z", "2026-11-15T09:14:03+02:00", StringComparison.Ordinal));

        Assert.Equal(TimeSpan.Zero, layout.SavedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), layout.SavedAt);
    }

    [Theory]
    [InlineData("icons", "id")]
    [InlineData("icons", "name")]
    [InlineData("icons", "x")]
    [InlineData("icons", "y")]
    [InlineData("monitors", "x")]
    [InlineData("monitors", "y")]
    [InlineData("monitors", "width")]
    [InlineData("monitors", "height")]
    [InlineData("monitors", "scale")]
    [InlineData("monitors", "primary")]
    [InlineData("previous.icons", "id")]
    [InlineData("previous.icons", "x")]
    public void AMissingOrNullValueIsAParseFailure(string list, string property)
    {
        Assert.Throws<JsonException>(() => Parse(Without(list, property, remove: true)));
        Assert.Throws<JsonException>(() => Parse(Without(list, property, remove: false)));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("name")]
    [InlineData("savedAt")]
    [InlineData("monitors")]
    [InlineData("icons")]
    public void AMissingTopLevelValueIsAParseFailure(string property)
    {
        JsonObject document = JsonNode.Parse(WithPrevious)!.AsObject();
        document.Remove(property);
        Assert.Throws<JsonException>(() => Parse(document.ToJsonString()));
    }

    [Theory]
    [InlineData("icons")]
    [InlineData("monitors")]
    [InlineData("previous.icons")]
    public void ANullElementIsAParseFailure(string list)
    {
        JsonObject document = JsonNode.Parse(WithPrevious)!.AsObject();
        JsonArray items = list == "previous.icons" ? document["previous"]!["icons"]!.AsArray() : document[list]!.AsArray();
        items.Add(null);
        Assert.Throws<JsonException>(() => Parse(document.ToJsonString()));
    }

    [Fact]
    public void ANullDocumentIsAParseFailure()
    {
        var error = Assert.Throws<JsonException>(() => Parse("null"));
        Assert.Equal("The document is null.", error.Message);
    }

    [Theory]
    [InlineData("07:14")]
    [InlineData("2026-11-15")]
    [InlineData("2026-11-15T07:14:03")]
    [InlineData("15 November 2026 07:14")]
    [InlineData("2026-11-15 07:14:03Z")]
    [InlineData("")]
    public void ATimeWithoutAFullDateTimeAndOffsetIsAParseFailure(string savedAt)
    {
        Assert.Throws<JsonException>(() => Parse(SpecExample.Replace("2026-11-15T07:14:03Z", savedAt, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("2026-11-15T07:14:03.1234567Z")]
    [InlineData("2026-11-15T08:14:03+01:00")]
    public void AFullDateTimeWithAnOffsetParses(string savedAt)
    {
        Layout layout = Parse(SpecExample.Replace("2026-11-15T07:14:03Z", savedAt, StringComparison.Ordinal));
        DateTimeOffset wholeSeconds = layout.SavedAt.AddTicks(-(layout.SavedAt.Ticks % TimeSpan.TicksPerSecond));
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), wholeSeconds);
        Assert.Equal(TimeSpan.Zero, layout.SavedAt.Offset);
    }

    [Theory]
    [InlineData("icons")]
    [InlineData("monitors")]
    [InlineData("previous.icons")]
    public void LayoutsThatDifferInOneCollectionAreNotEqual(string list)
    {
        JsonObject document = JsonNode.Parse(WithPrevious)!.AsObject();
        JsonArray items = list == "previous.icons" ? document["previous"]!["icons"]!.AsArray() : document[list]!.AsArray();
        items[0]!["x"] = (int)items[0]!["x"]! + 1;
        Assert.NotEqual(Parse(WithPrevious), Parse(document.ToJsonString()));
    }

    [Fact]
    public void AnIconAtZeroZeroParses()
    {
        Assert.Contains(Parse(WithPrevious).Icons, icon => icon is { X: 0, Y: 0, Name: "Zero.txt" });
    }

    [Fact]
    public void ALayoutKeepsItsItemsWhenTheCallersListChanges()
    {
        var icons = new List<IconPosition> { new("a", "A", 1, 2) };
        var layout = new Layout(1, "0123456789abcdef", "x", DateTimeOffset.UnixEpoch, [new MonitorInfo(0, 0, 1, 1, 100, true)], [.. icons], null);

        icons[0] = new IconPosition("b", "B", 3, 4);
        icons.Add(new IconPosition("c", "C", 5, 6));

        Assert.Equal([new IconPosition("a", "A", 1, 2)], layout.Icons);
    }

    [Fact]
    public void LayoutsWithEqualContentAreEqual()
    {
        Assert.Equal(Parse(WithPrevious), Parse(WithPrevious));
        Assert.Equal(Parse(WithPrevious).GetHashCode(), Parse(WithPrevious).GetHashCode());
        Assert.NotEqual(Parse(WithPrevious), Parse(SpecExample));
    }

    /// <summary>The WithPrevious document with <paramref name="property"/> of the first item in a list removed or nulled.</summary>
    private static string Without(string list, string property, bool remove)
    {
        JsonObject document = JsonNode.Parse(WithPrevious)!.AsObject();
        JsonArray items = list == "previous.icons" ? document["previous"]!["icons"]!.AsArray() : document[list]!.AsArray();
        JsonObject first = items[0]!.AsObject();
        if (remove)
        {
            first.Remove(property);
        }
        else
        {
            first[property] = null;
        }

        return document.ToJsonString();
    }
}
