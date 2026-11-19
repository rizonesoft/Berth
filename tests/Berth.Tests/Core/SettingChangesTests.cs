using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class SettingChangesTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 19, 8, 15, 30, TimeSpan.Zero));
    private const string Stamp = "2026-11-19T08:15:30Z";

    private static (TestData Data, BerthPaths Paths, SettingsStore Store, Log Log, SettingChanges Changes) Fresh()
    {
        var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, Clock);
        return (data, paths, store, log, new SettingChanges(store, log));
    }

    private static string[] ReadLogLines(BerthPaths paths) =>
        File.Exists(paths.LogFile) ? File.ReadAllLines(paths.LogFile) : [];

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, Clock);

        Assert.Throws<ArgumentNullException>(() => new SettingChanges(null!, log));
        Assert.Throws<ArgumentNullException>(() => new SettingChanges(store, null!));
    }

    [Fact]
    public void SetRestoreAutomaticallyOnMissingFileCreatesSettingsAndLogsInfo()
    {
        (TestData data, BerthPaths paths, _, _, SettingChanges changes) = Fresh();
        using (data)
        {
            SettingChange result = changes.SetRestoreAutomatically(true);

            Assert.Equal(new SettingChange(Applied: true, Value: true, Message: null), result);
            SettingsLoad reloaded = new SettingsStore(paths).Load();
            Assert.True(reloaded.Exists);
            Assert.Null(reloaded.Unreadable);
            Assert.Equal(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: null), reloaded.Value);
            Assert.Equal([$"{Stamp} INFO Restore automatically turned on."], ReadLogLines(paths));
        }
    }

    [Fact]
    public void SetRestoreAutomaticallyAppliesReadsBackPreservesWindowAndLogsBothDirections()
    {
        (TestData data, BerthPaths paths, SettingsStore store, _, SettingChanges changes) = Fresh();
        using (data)
        {
            var placement = new WindowPlacement(120, 80, 1024, 700);
            store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: placement));

            SettingChange turnedOn = changes.SetRestoreAutomatically(true);
            Assert.Equal(new SettingChange(Applied: true, Value: true, Message: null), turnedOn);
            Assert.Equal(
                new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: placement),
                new SettingsStore(paths).Load().Value);

            SettingChange turnedOff = changes.SetRestoreAutomatically(false);
            Assert.Equal(new SettingChange(Applied: true, Value: false, Message: null), turnedOff);
            Assert.Equal(
                new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: placement),
                new SettingsStore(paths).Load().Value);

            Assert.Equal(
                [
                    $"{Stamp} INFO Restore automatically turned on.",
                    $"{Stamp} INFO Restore automatically turned off.",
                ],
                ReadLogLines(paths));
        }
    }

    [Fact]
    public void ConcurrentWindowSaveBetweenInitialLoadAndStoreUpdateIsPreserved()
    {
        (TestData data, BerthPaths paths, SettingsStore store, Log log, _) = Fresh();
        using (data)
        {
            var initialWindow = new WindowPlacement(10, 20, 800, 600);
            var concurrentWindow = new WindowPlacement(300, 400, 1200, 800);
            store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: initialWindow));

            int saveAttempts = 0;
            var changes = new SettingChanges(store, log)
            {
                BeforeSave = attempt =>
                {
                    saveAttempts = attempt;
                    new SettingsStore(paths).Save(
                        new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: concurrentWindow));
                },
            };

            SettingChange result = changes.SetRestoreAutomatically(true);

            Assert.Equal(1, saveAttempts);
            Assert.Equal(new SettingChange(Applied: true, Value: true, Message: null), result);
            SettingsLoad reloaded = new SettingsStore(paths).Load();
            Assert.Equal(
                new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: concurrentWindow),
                reloaded.Value);
            Assert.Equal([$"{Stamp} INFO Restore automatically turned on."], ReadLogLines(paths));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentWindowSaveDuringReplaceRetriesAndPreservesPlacement(bool fileExistsInitially)
    {
        (TestData data, BerthPaths paths, SettingsStore store, Log log, _) = Fresh();
        using (data)
        {
            if (fileExistsInitially)
            {
                store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: new WindowPlacement(10, 20, 800, 600)));
            }

            var concurrentWindow = new WindowPlacement(320, 420, 1100, 720);
            int attempts = 0;
            var changes = new SettingChanges(store, log)
            {
                BeforeReplace = attempt =>
                {
                    attempts = attempt;
                    if (attempt == 1)
                    {
                        new SettingsStore(paths).Save(
                            new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: concurrentWindow));
                    }
                },
            };

            SettingChange result = changes.SetRestoreAutomatically(true);

            Assert.Equal(2, attempts);
            Assert.Equal(new SettingChange(Applied: true, Value: true, Message: null), result);
            Assert.Equal(
                new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: concurrentWindow),
                new SettingsStore(paths).Load().Value);
            Assert.Equal([$"{Stamp} INFO Restore automatically turned on."], ReadLogLines(paths));
        }
    }

    [Fact]
    public void ExhaustedConcurrentReplaceRetriesPreserveWinningFileAndLogFail()
    {
        (TestData data, BerthPaths paths, SettingsStore store, Log log, _) = Fresh();
        using (data)
        {
            store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: new WindowPlacement(10, 20, 800, 600)));

            int attempts = 0;
            var changes = new SettingChanges(store, log)
            {
                BeforeReplace = attempt =>
                {
                    attempts = attempt;
                    new SettingsStore(paths).Save(
                        new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: new WindowPlacement(100 + attempt, 20, 800, 600)));
                },
            };

            SettingChange result = changes.SetRestoreAutomatically(false);

            Assert.Equal(3, attempts);
            Assert.False(result.Applied);
            Assert.True(result.Value);
            Assert.NotNull(result.Message);
            Assert.StartsWith($"Berth could not write {paths.SettingsFile}: ", result.Message, StringComparison.Ordinal);
            Assert.Equal(
                new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: new WindowPlacement(103, 20, 800, 600)),
                new SettingsStore(paths).Load().Value);
            Assert.Equal([$"{Stamp} FAIL {result.Message}"], ReadLogLines(paths));
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReadOnlySettingsFileLeavesBytesUnchangedPreservesOldStateAndLogsOneFailLine(bool initialValue, bool requestedValue)
    {
        (TestData data, BerthPaths paths, SettingsStore store, _, SettingChanges changes) = Fresh();
        using (data)
        {
            var placement = new WindowPlacement(200, 150, 980, 640);
            store.Save(new Settings(Settings.CurrentSchemaVersion, initialValue, placement));
            byte[] beforeBytes = File.ReadAllBytes(paths.SettingsFile);

            File.SetAttributes(paths.SettingsFile, FileAttributes.ReadOnly);
            SettingChange result;
            try
            {
                result = changes.SetRestoreAutomatically(requestedValue);
            }
            finally
            {
                File.SetAttributes(paths.SettingsFile, FileAttributes.Normal);
            }

            Assert.False(result.Applied);
            Assert.Equal(initialValue, result.Value);
            Assert.NotNull(result.Message);
            Assert.StartsWith($"Berth could not write {paths.SettingsFile}: ", result.Message, StringComparison.Ordinal);
            Assert.EndsWith(".", result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("..", result.Message, StringComparison.Ordinal);
            Assert.Equal(beforeBytes, File.ReadAllBytes(paths.SettingsFile));
            Assert.Equal([$"{Stamp} FAIL {result.Message}"], ReadLogLines(paths));
        }
    }

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(false, null, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void UnreadableSettingsFileOnInitialLoadIsUntouchedPreservesRollbackStateAndLogsOneFailLine(
        bool requestedValue,
        bool? callerCurrent,
        bool expectedRollback)
    {
        (TestData data, BerthPaths paths, _, _, SettingChanges changes) = Fresh();
        using (data)
        {
            Directory.CreateDirectory(paths.DataRoot);
            File.WriteAllText(paths.SettingsFile, "{", new UTF8Encoding(false));
            byte[] beforeBytes = File.ReadAllBytes(paths.SettingsFile);

            SettingChange result = callerCurrent is { } current
                ? changes.SetRestoreAutomatically(requestedValue, current)
                : changes.SetRestoreAutomatically(requestedValue);

            Assert.Equal(new SettingChange(Applied: false, Value: expectedRollback, Message: Messages.UnreadableSettings), result);
            Assert.Equal(beforeBytes, File.ReadAllBytes(paths.SettingsFile));
            Assert.Equal([$"{Stamp} FAIL {Messages.UnreadableSettings}"], ReadLogLines(paths));
        }
    }

    [Fact]
    public void SettingsCorruptedAfterInitialLoadTriggerStoreUnreadableSettingsExceptionAndPreservePriorState()
    {
        (TestData data, BerthPaths paths, SettingsStore store, Log log, _) = Fresh();
        using (data)
        {
            store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: new WindowPlacement(100, 100, 980, 640)));
            byte[] malformed = [0xEF, 0xBB, 0xBF, (byte)'{'];

            var changes = new SettingChanges(store, log)
            {
                BeforeSave = _ => File.WriteAllBytes(paths.SettingsFile, malformed),
            };

            SettingChange result = changes.SetRestoreAutomatically(false);

            Assert.Equal(new SettingChange(Applied: false, Value: true, Message: Messages.UnreadableSettings), result);
            Assert.Equal(malformed, File.ReadAllBytes(paths.SettingsFile));
            Assert.Equal([$"{Stamp} FAIL {Messages.UnreadableSettings}"], ReadLogLines(paths));
        }
    }

    [Fact]
    public void PostSaveReadBackHoldingOldValueFailsPreservesOldStateAndLogsFail()
    {
        (TestData data, BerthPaths paths, SettingsStore store, Log log, _) = Fresh();
        using (data)
        {
            var placement = new WindowPlacement(50, 60, 980, 640);
            store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: placement));

            var changes = new SettingChanges(store, log)
            {
                AfterSave = () => new SettingsStore(paths).Save(
                    new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: false, Window: placement)),
            };

            SettingChange result = changes.SetRestoreAutomatically(true);

            string expected = Messages.WriteFailed(paths.SettingsFile, "The settings file read back after saving did not hold the new value.");
            Assert.Equal(new SettingChange(Applied: false, Value: false, Message: expected), result);
            Assert.Equal([$"{Stamp} FAIL {expected}"], ReadLogLines(paths));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PostSaveDeletionOrCorruptionDuringOffFromOnPreservesPriorTrueStateAndLogsFail(bool deleteFile)
    {
        (TestData data, BerthPaths paths, SettingsStore store, Log log, _) = Fresh();
        using (data)
        {
            store.Save(new Settings(Settings.CurrentSchemaVersion, RestoreAutomatically: true, Window: null));

            var changes = new SettingChanges(store, log)
            {
                AfterSave = () =>
                {
                    if (deleteFile)
                    {
                        File.Delete(paths.SettingsFile);
                    }
                    else
                    {
                        File.WriteAllText(paths.SettingsFile, "{", new UTF8Encoding(false));
                    }
                },
            };

            SettingChange result = changes.SetRestoreAutomatically(false);

            Assert.False(result.Applied);
            Assert.True(result.Value);
            Assert.NotNull(result.Message);
            Assert.StartsWith($"Berth could not write {paths.SettingsFile}: ", result.Message, StringComparison.Ordinal);
            Assert.Equal([$"{Stamp} FAIL {result.Message}"], ReadLogLines(paths));
        }
    }

    [Fact]
    public void RecordStartWithWindowsReturnsExactStateAndLogsInfoOrFailAcrossDirections()
    {
        (TestData data, BerthPaths paths, _, _, SettingChanges changes) = Fresh();
        using (data)
        {
            SettingChange enabled = changes.RecordStartWithWindows(true, failure: null);
            SettingChange disabled = changes.RecordStartWithWindows(false, failure: null);
            SettingChange enableFailed = changes.RecordStartWithWindows(true, new UnauthorizedAccessException("Access to the registry key is denied."));
            SettingChange disableFailed = changes.RecordStartWithWindows(false, new IOException("Key busy.\r\nRetry.. "));

            Assert.Equal(new SettingChange(Applied: true, Value: true, Message: null), enabled);
            Assert.Equal(new SettingChange(Applied: true, Value: false, Message: null), disabled);

            const string ExpectedEnableFailure = "Berth could not change the Start with Windows setting: Access to the registry key is denied.";
            const string ExpectedDisableFailure = "Berth could not change the Start with Windows setting: Key busy. Retry.";
            Assert.Equal(new SettingChange(Applied: false, Value: false, Message: ExpectedEnableFailure), enableFailed);
            Assert.Equal(new SettingChange(Applied: false, Value: true, Message: ExpectedDisableFailure), disableFailed);

            Assert.Equal(
                [
                    $"{Stamp} INFO Start with Windows turned on.",
                    $"{Stamp} INFO Start with Windows turned off.",
                    $"{Stamp} FAIL {ExpectedEnableFailure}",
                    $"{Stamp} FAIL {ExpectedDisableFailure}",
                ],
                ReadLogLines(paths));
        }
    }
}
