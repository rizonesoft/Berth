using System.Collections.Immutable;
using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

/// <summary>Layouts whose keys are their monitors' keys, as every stored layout must be.</summary>
internal static class Layouts
{
    public static readonly MonitorInfo One = new(0, 0, 2560, 1440, 100, true);
    public static readonly MonitorInfo Left = new(-1920, 0, 1920, 1080, 100, false);

    public static Layout Make(string name, MonitorInfo[] monitors, IconPosition[] icons, PreviousSave? previous = null) =>
        new(1, ArrangementKey.From(monitors), name, new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), [.. monitors], [.. icons], previous);

    public static Layout Single() => Make("1 monitor", [One], [new IconPosition(@"C:\Desk\a.txt", "a.txt", 10, 20)]);

    public static Layout Double() => Make("2 monitors", [Left, One], [new IconPosition(@"C:\Desk\b.txt", "b.txt", -1800, 40)]);
}

public sealed class LayoutRulesTests
{
    [Fact]
    public void AValidLayoutHasNoProblem()
    {
        Layout layout = Layouts.Single();
        Assert.Null(LayoutRules.Check(layout, layout.Key));
    }

[Theory]
    [InlineData("schema")]
    [InlineData("fileKey")]
    [InlineData("monitorsKey")]
    [InlineData("noMonitors")]
    [InlineData("zeroWidth")]
    [InlineData("zeroHeight")]
    [InlineData("zeroScale")]
    [InlineData("emptyName")]
    [InlineData("spaceName")]
    [InlineData("paddedName")]
    [InlineData("longName")]
    [InlineData("emptyId")]
    [InlineData("spaceId")]
    [InlineData("duplicateIds")]
    [InlineData("duplicatePreviousIds")]
    public void EachRuleRefusesItsCase(string rule)
    {
        Layout good = Layouts.Single();
        IconPosition icon = good.Icons[0];
        (Layout layout, string fileKey) = rule switch
        {
            "schema" => (good with { SchemaVersion = 2 }, good.Key),
            "fileKey" => (good, "0000000000000000"),
            "monitorsKey" => (good with { Monitors = [Layouts.One with { Width = 1920 }] }, good.Key),
            "noMonitors" => (good with { Monitors = [] }, good.Key),
            "zeroWidth" => (WithMonitor(good, Layouts.One with { Width = 0 }), Key(Layouts.One with { Width = 0 })),
            "zeroHeight" => (WithMonitor(good, Layouts.One with { Height = 0 }), Key(Layouts.One with { Height = 0 })),
            "zeroScale" => (WithMonitor(good, Layouts.One with { Scale = 0 }), Key(Layouts.One with { Scale = 0 })),
            "emptyName" => (good with { Name = "" }, good.Key),
            "spaceName" => (good with { Name = "   " }, good.Key),
            "paddedName" => (good with { Name = " Docked" }, good.Key),
            "longName" => (good with { Name = new string('n', LayoutRules.MaxNameLength + 1) }, good.Key),
            "emptyId" => (good with { Icons = [icon with { Id = "" }] }, good.Key),
            "spaceId" => (good with { Icons = [icon with { Id = "  " }] }, good.Key),
            "duplicateIds" => (good with { Icons = [icon, icon with { Id = icon.Id.ToUpperInvariant(), X = 1 }] }, good.Key),
            _ => (good with { Previous = new PreviousSave(good.SavedAt, [icon, icon with { Id = icon.Id.ToLowerInvariant() }]) }, good.Key),
        };

        Assert.False(string.IsNullOrWhiteSpace(LayoutRules.Check(layout, fileKey)));
    }

    [Fact]
    public void ANameOfFortyCharactersIsFine()
    {
        Layout layout = Layouts.Single() with { Name = new string('n', LayoutRules.MaxNameLength) };
        Assert.Null(LayoutRules.Check(layout, layout.Key));
    }

    [Fact]
    public void TheSameIdOnceInEachSaveIsFine()
    {
        Layout good = Layouts.Single();
        Layout layout = good with { Previous = new PreviousSave(good.SavedAt.AddDays(-1), [good.Icons[0] with { X = 99 }]) };
        Assert.Null(LayoutRules.Check(layout, layout.Key));
    }

    private static string Key(MonitorInfo monitor) => ArrangementKey.From([monitor]);

    private static Layout WithMonitor(Layout layout, MonitorInfo monitor) => layout with { Monitors = [monitor], Key = Key(monitor) };
}

public sealed class LayoutStoreTests
{
    private static (TestData Data, BerthPaths Paths, LayoutStore Store) Fresh()
    {
        var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        return (data, paths, new LayoutStore(paths));
    }

    private static void Put(BerthPaths paths, string fileName, string content)
    {
        Directory.CreateDirectory(paths.LayoutsFolder);
        File.WriteAllText(Path.Combine(paths.LayoutsFolder, fileName), content, new UTF8Encoding(false));
    }

    private static string Json(Layout layout) => System.Text.Json.JsonSerializer.Serialize(layout, BerthJson.Default.Layout);

    [Fact]
    public void AFreshProfileHasNoLayoutsAndNoFolderIsMade()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            LayoutSet set = store.LoadAll();
            Assert.Empty(set.Layouts);
            Assert.Empty(set.Unreadable);
            Assert.False(Directory.Exists(paths.LayoutsFolder));
        }
    }

    [Fact]
    public void AFolderThatCannotBeListedIsAnError()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Directory.CreateDirectory(paths.DataRoot);
            File.WriteAllText(paths.LayoutsFolder, "a file where the folder should be");
            Assert.ThrowsAny<IOException>(() => store.LoadAll());
        }
    }

    [Fact]
    public void LayoutsSurviveInAFreshStoreWithEveryValue()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout single = Layouts.Single();
            Layout two = Layouts.Double();
            store.Save(single);
            store.Save(two);
            Layout moved = single with
            {
                SavedAt = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero),
                Icons = [new IconPosition(@"C:\Desk\a.txt", "a.txt", 300, 400)],
                Previous = new PreviousSave(single.SavedAt, single.Icons),
            };
            store.Save(moved);

            var reopened = new LayoutStore(BerthPaths.ForRoot(data.Root));
            LayoutSet set = reopened.LoadAll();

            Assert.Empty(set.Unreadable);
            var expectedSingle = new Layout(
                1,
                "c7f017905ce518e6",
                "1 monitor",
                new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero),
                [new MonitorInfo(0, 0, 2560, 1440, 100, true)],
                [new IconPosition(@"C:\Desk\a.txt", "a.txt", 300, 400)],
                new PreviousSave(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), [new IconPosition(@"C:\Desk\a.txt", "a.txt", 10, 20)]));
            var expectedTwo = new Layout(
                1,
                ArrangementKey.From([new MonitorInfo(-1920, 0, 1920, 1080, 100, false), new MonitorInfo(0, 0, 2560, 1440, 100, true)]),
                "2 monitors",
                new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero),
                [new MonitorInfo(-1920, 0, 1920, 1080, 100, false), new MonitorInfo(0, 0, 2560, 1440, 100, true)],
                [new IconPosition(@"C:\Desk\b.txt", "b.txt", -1800, 40)],
                null);
            Assert.Equal([expectedSingle, expectedTwo], set.Layouts.OrderBy(l => l.Name, StringComparer.Ordinal));
            Assert.True(File.Exists(paths.LayoutFile("c7f017905ce518e6")));

            Assert.True(reopened.Delete("c7f017905ce518e6"));
            LayoutSet after = new LayoutStore(BerthPaths.ForRoot(data.Root)).LoadAll();
            Assert.Equal([two.Key], after.Layouts.Select(l => l.Key));
        }
    }

    [Fact]
    public void EveryBadFileIsReportedBesideTheGoodOnes()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            store.Save(Layouts.Single());
            store.Save(Layouts.Double());
            Put(paths, "0123456789abcdef.json", "{");
            Put(paths, "fedcba9876543210.json", Json(Layouts.Single()));

            LayoutSet set = store.LoadAll();

            Assert.Equal(2, set.Layouts.Count);
            Assert.Equal(["0123456789abcdef.json", "fedcba9876543210.json"], set.Unreadable.Select(u => u.FileName));
            Assert.All(set.Unreadable, u => Assert.False(string.IsNullOrWhiteSpace(u.Reason)));
        }
    }

    [Fact]
    public void ALayoutWhoseMonitorsDoNotGiveItsKeyIsReportedNotFound()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            // Key and file name agree, but the monitors are another arrangement's.
            Layout lying = Layouts.Single() with { Monitors = [Layouts.One with { Width = 1920 }] };
            Put(paths, lying.Key + ".json", Json(lying));
            byte[] before = File.ReadAllBytes(paths.LayoutFile(lying.Key));

            Assert.Equal([lying.Key + ".json"], store.LoadAll().Unreadable.Select(u => u.FileName));
            Assert.Throws<UnreadableLayoutException>(() => store.Find(lying.Key));
            Assert.Equal(before, File.ReadAllBytes(paths.LayoutFile(lying.Key)));
        }
    }

    [Fact]
    public void NonJsonFilesAreIgnored()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Put(paths, "x.json.tmp-1", "{");
            Put(paths, "notes.txt", "hello");
            Put(paths, "UPPER.JSON", "{");

            LayoutSet set = store.LoadAll();

            Assert.Empty(set.Layouts);
            Assert.Equal(["UPPER.JSON"], set.Unreadable.Select(u => u.FileName));
        }
    }

[Theory]
    [InlineData("brace")]
    [InlineData("schema2")]
    [InlineData("keyMismatch")]
    [InlineData("monitorsKey")]
    [InlineData("locked")]
    public void ASaveOverAnUnreadableFileIsRefusedAndTheFileKept(string kind)
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout layout = Layouts.Single();
            string content = kind switch
            {
                "brace" => "{",
                "schema2" => Json(layout).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal),
                "keyMismatch" => Json(layout).Replace(layout.Key, "0123456789abcdef", StringComparison.Ordinal),
                "monitorsKey" => Json(layout with { Monitors = [Layouts.One with { Width = 1920 }] }),
                _ => Json(layout),
            };
            Put(paths, layout.Key + ".json", content);
            string path = paths.LayoutFile(layout.Key);
            byte[] before = File.ReadAllBytes(path);

            UnreadableLayoutException error;
            using (kind == "locked" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
            {
                error = Assert.Throws<UnreadableLayoutException>(() => store.Save(layout with { Name = "Desk" }));
            }

            Assert.Equal(layout.Key + ".json", error.FileName);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void AnInvalidLayoutIsNeverWritten()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout good = Layouts.Single();
            store.Save(good);
            byte[] before = File.ReadAllBytes(paths.LayoutFile(good.Key));

            Assert.Throws<ArgumentException>(() => store.Save(good with { Name = "" }));
            Assert.Equal(before, File.ReadAllBytes(paths.LayoutFile(good.Key)));
        }
    }

    [Fact]
    public void FindReturnsEachLayoutNullForNoneAndRefusesAnUnreadableOne()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            store.Save(Layouts.Single());
            store.Save(Layouts.Double());
            Put(paths, "0123456789abcdef.json", "{");

            Assert.Equal("1 monitor", store.Find(Layouts.Single().Key)!.Name);
            Assert.Equal("2 monitors", store.Find(Layouts.Double().Key)!.Name);
            Assert.Null(store.Find("fedcba9876543210"));
            UnreadableLayoutException error = Assert.Throws<UnreadableLayoutException>(() => store.Find("0123456789abcdef"));
            Assert.Equal("0123456789abcdef.json", error.FileName);
            Assert.Contains(store.LoadAll().Unreadable, u => u.FileName == "0123456789abcdef.json");
        }
    }

    [Fact]
    public void DeleteIsFalseForNoneAndRefusesAFileThatBecameUnreadable()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Assert.False(store.Delete("fedcba9876543210"));

            Layout layout = Layouts.Single();
            store.Save(layout);
            Assert.Single(store.LoadAll().Layouts);
            File.WriteAllText(paths.LayoutFile(layout.Key), "{");
            byte[] before = File.ReadAllBytes(paths.LayoutFile(layout.Key));

            UnreadableLayoutException error = Assert.Throws<UnreadableLayoutException>(() => store.Delete(layout.Key));

            Assert.Equal(layout.Key + ".json", error.FileName);
            Assert.Equal(before, File.ReadAllBytes(paths.LayoutFile(layout.Key)));
        }
    }

    [Fact]
    public void AFileWhoseNameIsNotTheLowercaseKeyIsRefusedByEveryOperation()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout layout = Layouts.Single();
            Put(paths, layout.Key.ToUpperInvariant() + ".json", Json(layout));
            string path = Path.Combine(paths.LayoutsFolder, layout.Key.ToUpperInvariant() + ".json");
            byte[] before = File.ReadAllBytes(path);

            Assert.Empty(store.LoadAll().Layouts);
            Assert.Equal([layout.Key.ToUpperInvariant() + ".json"], store.LoadAll().Unreadable.Select(u => u.FileName));
            Assert.Equal(layout.Key.ToUpperInvariant() + ".json", Assert.Throws<UnreadableLayoutException>(() => store.Find(layout.Key)).FileName);
            Assert.Throws<UnreadableLayoutException>(() => store.Save(layout with { Name = "Desk" }));
            Assert.Throws<UnreadableLayoutException>(() => store.Delete(layout.Key));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal([layout.Key.ToUpperInvariant() + ".json"], Directory.GetFiles(paths.LayoutsFolder).Select(Path.GetFileName));
        }
    }

    [Fact]
    public void ADanglingLinkIsReportedNotIgnored()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout layout = Layouts.Single();
            Directory.CreateDirectory(paths.LayoutsFolder);
            string target = Path.Combine(data.Root, "gone.json");
            File.WriteAllText(target, Json(layout));
            File.CreateSymbolicLink(paths.LayoutFile(layout.Key), target);
            File.Delete(target);

            Assert.Equal([layout.Key + ".json"], store.LoadAll().Unreadable.Select(u => u.FileName));
            Assert.Throws<UnreadableLayoutException>(() => store.Find(layout.Key));
            Assert.Throws<UnreadableLayoutException>(() => store.Save(layout with { Name = "Desk" }));
            Assert.Throws<UnreadableLayoutException>(() => store.Delete(layout.Key));
            Assert.NotNull(new FileInfo(paths.LayoutFile(layout.Key)).LinkTarget);
        }
    }

    [Fact]
    public void ALiveLinkIsRefusedTooBecauseBerthNeverWritesOne()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout layout = Layouts.Single();
            Directory.CreateDirectory(paths.LayoutsFolder);
            string target = Path.Combine(data.Root, "elsewhere.json");
            File.WriteAllText(target, Json(layout));
            File.CreateSymbolicLink(paths.LayoutFile(layout.Key), target);
            byte[] before = File.ReadAllBytes(target);

            Assert.Empty(store.LoadAll().Layouts);
            Assert.Contains("link", Assert.Single(store.LoadAll().Unreadable).Reason, StringComparison.Ordinal);
            Assert.Throws<UnreadableLayoutException>(() => store.Save(layout with { Name = "Desk" }));
            Assert.Equal(before, File.ReadAllBytes(target));
        }
    }

    [Fact]
    public void AFolderThatMayNotBeListedIsAnErrorNotAFreshProfile()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            Layout layout = Layouts.Single();
            store.Save(layout);
            byte[] before = File.ReadAllBytes(paths.LayoutFile(layout.Key));

            // The folder's attributes cannot be read and it cannot be listed, as when a redirected profile share
            // withdraws access: Directory.Exists then says false, and that must not read as a fresh profile.
            var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            var denyFolder = new System.Security.AccessControl.FileSystemAccessRule(me,
                System.Security.AccessControl.FileSystemRights.ListDirectory | System.Security.AccessControl.FileSystemRights.ReadAttributes,
                System.Security.AccessControl.AccessControlType.Deny);
            var denyParent = new System.Security.AccessControl.FileSystemAccessRule(me, System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.AccessControlType.Deny);
            var folder = new DirectoryInfo(paths.LayoutsFolder);
            var parent = new DirectoryInfo(paths.DataRoot);
            System.Security.AccessControl.DirectorySecurity folderRules = folder.GetAccessControl();
            System.Security.AccessControl.DirectorySecurity parentRules = parent.GetAccessControl();
            folderRules.AddAccessRule(denyFolder);
            parentRules.AddAccessRule(denyParent);
            folder.SetAccessControl(folderRules);
            parent.SetAccessControl(parentRules);
            try
            {
                Assert.False(Directory.Exists(paths.LayoutsFolder));
                Assert.ThrowsAny<Exception>(() => store.LoadAll());
                foreach (Action operation in new Action[] { () => store.LoadAll(), () => store.Find(layout.Key), () => store.Delete(layout.Key), () => store.Save(layout with { Name = "Desk" }) })
                {
                    Exception? error = Record.Exception(operation);
                    Assert.True(error is UnauthorizedAccessException or IOException, error?.GetType().FullName ?? "no exception");
                }
            }
            finally
            {
                parentRules.RemoveAccessRule(denyParent);
                parent.SetAccessControl(parentRules);
                folderRules.RemoveAccessRule(denyFolder);
                folder.SetAccessControl(folderRules);
            }

            Assert.Equal(before, File.ReadAllBytes(paths.LayoutFile(layout.Key)));
        }
    }

    [Fact]
    public void AFileGoneBetweenListingAndReadingIsStillReported()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            store.Save(Layouts.Single());
            store.Save(Layouts.Double());

            LayoutSet set = store.LoadAll(afterList: () => File.Delete(paths.LayoutFile(Layouts.Single().Key)));

            Assert.Equal([Layouts.Double().Key], set.Layouts.Select(l => l.Key));
            UnreadableFile gone = Assert.Single(set.Unreadable);
            Assert.Equal(Layouts.Single().Key + ".json", gone.FileName);
            Assert.False(string.IsNullOrWhiteSpace(gone.Reason));
        }
    }

    [Fact]
    public void AFirstSaveMakesItsOwnFolder()
    {
        (TestData data, BerthPaths paths, LayoutStore store) = Fresh();
        using (data)
        {
            store.Save(Layouts.Single());
            Assert.True(File.Exists(paths.LayoutFile(Layouts.Single().Key)));
            Assert.False(Directory.Exists(paths.LocalRoot));
        }
    }
}
