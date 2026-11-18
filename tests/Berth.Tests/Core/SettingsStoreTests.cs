using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class SettingsStoreTests
{
    private const string Good = """{ "schemaVersion": 1, "restoreAutomatically": true, "window": { "x": 100, "y": 100, "width": 980, "height": 640 } }""";

    private static (TestData Data, BerthPaths Paths, SettingsStore Store) Fresh()
    {
        var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        return (data, paths, new SettingsStore(paths));
    }

    private static void Put(BerthPaths paths, string content)
    {
        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(paths.SettingsFile, content, new UTF8Encoding(false));
    }

    [Fact]
    public void TheDefaultsHaveTheSpecShape()
    {
        JsonNode written = JsonNode.Parse(JsonSerializer.Serialize(Settings.Default, BerthJson.Default.Settings))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "schemaVersion": 1, "restoreAutomatically": false, "window": null }"""), written), written.ToJsonString());
    }

    [Fact]
    public void AMissingFileIsAFirstRunWithTheDefaults()
    {
        (TestData data, _, SettingsStore store) = Fresh();
        using (data)
        {
            Assert.Equal(new SettingsLoad(Settings.Default, Exists: false, Unreadable: null), store.Load());
        }
    }

    [Fact]
    public void AGoodFileGivesItsValues()
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            Put(paths, Good);
            Assert.Equal(new SettingsLoad(new Settings(1, true, new WindowPlacement(100, 100, 980, 640)), Exists: true, Unreadable: null), store.Load());
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{ "schemaVersion": 2, "restoreAutomatically": false, "window": null }""")]
    [InlineData("""{ "schemaVersion": 1, "window": null }""")]
    [InlineData("""{ "schemaVersion": 1, "restoreAutomatically": "yes", "window": null }""")]
    [InlineData("""{ "schemaVersion": 1, "restoreAutomatically": false }""")]
    [InlineData("""{ "schemaVersion": 1, "restoreAutomatically": false, "window": { "y": 1, "width": 980, "height": 640 } }""")]
    [InlineData("""{ "schemaVersion": 1, "restoreAutomatically": false, "window": { "x": 1, "y": 1, "width": 0, "height": 640 } }""")]
    [InlineData("null")]
    public void AnUnreadableFileExistsGivesTheDefaultsAndIsNeverReplaced(string content)
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            Put(paths, content);
            byte[] before = File.ReadAllBytes(paths.SettingsFile);

            SettingsLoad load = store.Load();
            Assert.Equal(Settings.Default, load.Value);
            Assert.True(load.Exists);
            Assert.False(string.IsNullOrWhiteSpace(load.Unreadable));

            UnreadableSettingsException error = Assert.Throws<UnreadableSettingsException>(() => store.Save(Settings.Default with { RestoreAutomatically = true }));
            Assert.Equal("settings.json", UnreadableSettingsException.FileName);
            Assert.Equal(load.Unreadable, error.Reason);
            Assert.Equal(before, File.ReadAllBytes(paths.SettingsFile));
        }
    }

    [Fact]
    public void ADamagedByteInAValueBerthDoesNotReadStillMakesTheFileUnreadable()
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("""{"schemaVersion":1,"restoreAutomatically":true,"window":null,"extra":"X"}""");
            bytes[Array.LastIndexOf(bytes, (byte)'X')] = 0xFF;
            Directory.CreateDirectory(paths.DataRoot);
            File.WriteAllBytes(paths.SettingsFile, bytes);

            SettingsLoad load = store.Load();
            Assert.True(load.Exists);
            Assert.Equal(Settings.Default, load.Value);
            Assert.Contains("UTF-8", load.Unreadable, StringComparison.Ordinal);
            Assert.Throws<UnreadableSettingsException>(() => store.Save(Settings.Default));
            Assert.Equal(bytes, File.ReadAllBytes(paths.SettingsFile));
        }
    }
    [Fact]
    public void ALockedFileExistsAndIsNeverReplaced()
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            Put(paths, Good);
            byte[] before = File.ReadAllBytes(paths.SettingsFile);
            using (new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                SettingsLoad load = store.Load();
                Assert.Equal(Settings.Default, load.Value);
                Assert.True(load.Exists);
                Assert.False(string.IsNullOrWhiteSpace(load.Unreadable));
                Assert.Throws<UnreadableSettingsException>(() => store.Save(Settings.Default));
            }

            Assert.Equal(before, File.ReadAllBytes(paths.SettingsFile));
        }
    }

    [Fact]
    public void AWriteFailureIsNotWordedAsAnUnreadableFile()
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            Put(paths, Good);
            File.SetAttributes(paths.SettingsFile, FileAttributes.ReadOnly);
            try
            {
                Exception error = Assert.ThrowsAny<Exception>(() => store.Save(Settings.Default));
                Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().FullName);
            }
            finally
            {
                File.SetAttributes(paths.SettingsFile, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void SettingsSurviveInAFreshStoreAndCanBeCleared()
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            store.Save(new Settings(1, true, new WindowPlacement(-1600, 120, 1100, 700)));

            JsonNode expected = JsonNode.Parse("""{ "schemaVersion": 1, "restoreAutomatically": true, "window": { "x": -1600, "y": 120, "width": 1100, "height": 700 } }""")!;
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(paths.SettingsFile))), File.ReadAllText(paths.SettingsFile));
            SettingsLoad load = new SettingsStore(BerthPaths.ForRoot(data.Root)).Load();
            Assert.Equal(new SettingsLoad(new Settings(1, true, new WindowPlacement(-1600, 120, 1100, 700)), Exists: true, Unreadable: null), load);

            new SettingsStore(BerthPaths.ForRoot(data.Root)).Save(new Settings(1, false, null));
            Assert.Equal(new SettingsLoad(new Settings(1, false, null), Exists: true, Unreadable: null), new SettingsStore(BerthPaths.ForRoot(data.Root)).Load());
        }
    }

    [Fact]
    public void AnInvalidValueIsNeverWritten()
    {
        (TestData data, BerthPaths paths, SettingsStore store) = Fresh();
        using (data)
        {
            Assert.Throws<ArgumentException>(() => store.Save(new Settings(1, false, new WindowPlacement(0, 0, 0, 100))));
            Assert.Throws<ArgumentException>(() => store.Save(new Settings(2, false, null)));
            Assert.False(File.Exists(paths.SettingsFile));
        }
    }
}
