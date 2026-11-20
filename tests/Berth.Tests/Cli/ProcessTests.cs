using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.Desktop;

namespace Berth.Tests.Cli;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed class ProcessTests
{
    [Fact]
    public void NonMovingCommandsMatchSpec13StreamsExitCodesAndLeaveOwnerDataUntouched()
    {
        OwnerDataSnapshot before = OwnerDataSnapshot.Capture();

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);

        // 1) --version (literal SPEC §13 string)
        BerthProcessResult version = BerthProcess.Run(data.Root, ["--version"]);
        Assert.Equal(0, version.ExitCode);
        Assert.Equal($"Berth 0.1.0{Environment.NewLine}", version.StandardOutput);
        Assert.Equal(string.Empty, version.StandardError);

        // 2) --help
        BerthProcessResult help = BerthProcess.Run(data.Root, ["--help"]);
        Assert.Equal(0, help.ExitCode);
        Assert.Equal(Usage.Text + Environment.NewLine, help.StandardOutput);
        Assert.Contains("berth restore", help.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(string.Empty, help.StandardError);

        // 3) unknown command ("frobnicate")
        BerthProcessResult bad = BerthProcess.Run(data.Root, ["frobnicate"]);
        Assert.Equal(2, bad.ExitCode);
        Assert.Equal(string.Empty, bad.StandardOutput);
        Assert.Equal(
            $"berth: unknown command \"frobnicate\"{Environment.NewLine}" +
            $"Run \"berth --help\" for the commands.{Environment.NewLine}",
            bad.StandardError);

        // 4) list on a fresh data folder
        BerthProcessResult list = BerthProcess.Run(data.Root, ["list"]);
        Assert.Equal(0, list.ExitCode);
        Assert.Equal($"No layouts saved yet.{Environment.NewLine}", list.StandardOutput);
        Assert.Equal(string.Empty, list.StandardError);

        // None of these four commands perform an operation or fail, so no log file is created.
        Assert.False(File.Exists(paths.LogFile), "Non-moving standalone/list commands must not create a log file.");

        before.AssertUnchanged();
    }

    [Fact]
    public void RestoreOnFreshDataFolderExits3WithExactSpec18StderrOneWarnLogAndUntouchedOwnerData()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");
        OwnerDataSnapshot before = OwnerDataSnapshot.Capture();

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        string uniquePrefix = $"berth-test-refusal-{Guid.NewGuid():N}";

        BerthProcessResult result = BerthProcess.Run(data.Root, ["restore"], testIconPrefix: uniquePrefix);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal($"This monitor arrangement has no saved layout.{Environment.NewLine}", result.StandardError);

        Assert.True(File.Exists(paths.LogFile), "Expected refusal to write a WARN line to local\\logs\\berth.log.");
        string[] logLines = File.ReadAllLines(paths.LogFile);
        string soleLine = Assert.Single(logLines);
        AssertLogLine(soleLine, "WARN", "This monitor arrangement has no saved layout.");

        before.AssertUnchanged();
    }

    [Fact]
    public void ListWithBlockedLayoutsFolderExits1WithExactReadFailedStderrOneFailLogAndUntouchedOwnerData()
    {
        OwnerDataSnapshot before = OwnerDataSnapshot.Capture();

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(paths.LayoutsFolder, "not-a-directory");

        // Independently obtain the OS reason for enumerating the regular-file fixture path.
        IOException osError = Assert.ThrowsAny<IOException>(() => _ = Directory.GetFiles(paths.LayoutsFolder));
        string expectedSentence = $"Berth could not read {paths.LayoutsFolder}: {osError.Message.TrimEnd('.', ' ')}.";

        BerthProcessResult result = BerthProcess.Run(data.Root, ["list"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(expectedSentence + Environment.NewLine, result.StandardError);

        Assert.True(File.Exists(paths.LogFile), "Expected folder-read failure to write a FAIL line to local\\logs\\berth.log.");
        string[] logLines = File.ReadAllLines(paths.LogFile);
        string soleLine = Assert.Single(logLines);
        AssertLogLine(soleLine, "FAIL", expectedSentence);

        before.AssertUnchanged();
    }

    [Fact]
    public void SaveMoveRestoreUndoAndListCycleOnProbeIconMatchesSpec13EndToEnd()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");
        OwnerDataSnapshot before = OwnerDataSnapshot.Capture();

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var monitors = new SystemMonitors();
        var helperLog = new Log(BerthPaths.ForRoot(Path.Combine(data.Root, "helper")), TimeProvider.System);
        using var shell = new ShellDesktopIcons(monitors, helperLog);

        Assert.SkipWhen(shell.AutoArrange, "Auto arrange icons is on; skipping live desktop movement cycle.");

        string probeFilePath;
        using (ProbeIcon probe = ProbeIcon.Create(shell))
        {
            probeFilePath = probe.FilePath;
            string prefix = probe.Prefix;

            // 1) berth save (exit 0) -> single layout file listing ONLY the probe icon.
            BerthProcessResult save = BerthProcess.Run(data.Root, ["save"], testIconPrefix: prefix);
            Assert.Equal(0, save.ExitCode);
            Assert.Equal(string.Empty, save.StandardError);
            Assert.StartsWith("Saved 1 icon to \"", save.StandardOutput, StringComparison.Ordinal);
            Assert.EndsWith($"\".{Environment.NewLine}", save.StandardOutput, StringComparison.Ordinal);

            string[] layoutFiles = Directory.GetFiles(paths.LayoutsFolder, "*.json");
            Assert.Single(layoutFiles);
            LayoutSet loaded = new LayoutStore(paths).LoadAll();
            Assert.Empty(loaded.Unreadable);
            Layout savedLayout = Assert.Single(loaded.Layouts);
            string layoutName = savedLayout.Name;
            Assert.Equal($"Saved 1 icon to \"{layoutName}\".{Environment.NewLine}", save.StandardOutput);

            IconPosition savedIcon = Assert.Single(savedLayout.Icons);
            Assert.Equal(probe.Icon.Id, savedIcon.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(probe.Icon.Name, savedIcon.Name);
            Assert.Equal(probe.Icon.X, savedIcon.X);
            Assert.Equal(probe.Icon.Y, savedIcon.Y);

            // 2) Move the probe icon horizontally onto an exact grid cell (when snap-to-grid is on) so Explorer does not
            // re-snap it asynchronously between our read and berth.exe restore.
            (bool snapToGrid, int spacingX, _) = ProbeIconTests.ReadGridInfo(shell);
            int stepX = snapToGrid && spacingX > 0
                ? Math.Max(spacingX * 2, ((200 + spacingX - 1) / spacingX) * spacingX)
                : 200;
            (int targetX, int targetY) = ProbeIconTests.ChooseInBoundsTarget(savedIcon.X, savedIcon.Y, stepX, 0, monitors.Read());
            probe.ScopedDesktop.SetPositions([savedIcon with { X = targetX, Y = targetY }]);

            IconPosition landed = WaitForDistinctPosition(probe.ScopedDesktop, savedIcon, TimeSpan.FromSeconds(5));
            Assert.True(landed.X != savedIcon.X || landed.Y != savedIcon.Y, "Probe icon did not move to a distinct position.");

            // 3) berth restore (exit 0) -> returns probe to exact saved coordinates and writes undo.json with landed coordinates.
            BerthProcessResult restore = BerthProcess.Run(data.Root, ["restore"], testIconPrefix: prefix);
            Assert.Equal(0, restore.ExitCode);
            Assert.Equal($"Restored 1 icon from \"{layoutName}\".{Environment.NewLine}", restore.StandardOutput);
            Assert.Equal(string.Empty, restore.StandardError);

            IconPosition afterRestore = Assert.Single(probe.ScopedDesktop.ReadIcons());
            Assert.Equal(savedIcon.Id, afterRestore.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(savedIcon.X, afterRestore.X);
            Assert.Equal(savedIcon.Y, afterRestore.Y);

            Assert.True(File.Exists(paths.UndoFile), "Expected restore to create local\\undo.json.");
            UndoSnapshot? snapshot = JsonSerializer.Deserialize(File.ReadAllText(paths.UndoFile), BerthJson.Default.UndoSnapshot);
            Assert.NotNull(snapshot);
            IconPosition undoIcon = Assert.Single(snapshot.Icons);
            Assert.Equal(landed.Id, undoIcon.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(landed.X, undoIcon.X);
            Assert.Equal(landed.Y, undoIcon.Y);

            // 4) berth undo (exit 0) -> returns probe to exact landed coordinates and deletes undo.json.
            BerthProcessResult undo = BerthProcess.Run(data.Root, ["undo"], testIconPrefix: prefix);
            Assert.Equal(0, undo.ExitCode);
            Assert.Equal($"Put 1 icon back.{Environment.NewLine}", undo.StandardOutput);
            Assert.Equal(string.Empty, undo.StandardError);

            IconPosition afterUndo = Assert.Single(probe.ScopedDesktop.ReadIcons());
            Assert.Equal(landed.Id, afterUndo.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(landed.X, afterUndo.X);
            Assert.Equal(landed.Y, afterUndo.Y);
            Assert.False(File.Exists(paths.UndoFile), "Expected successful undo to delete local\\undo.json.");

            // 5) berth undo again (exit 3) -> refused with "There is nothing to undo."
            BerthProcessResult secondUndo = BerthProcess.Run(data.Root, ["undo"], testIconPrefix: prefix);
            Assert.Equal(3, secondUndo.ExitCode);
            Assert.Equal(string.Empty, secondUndo.StandardOutput);
            Assert.Equal($"There is nothing to undo.{Environment.NewLine}", secondUndo.StandardError);

            // 6) berth list (exit 0) -> exactly one active '* ' row.
            BerthProcessResult list = BerthProcess.Run(data.Root, ["list"], testIconPrefix: prefix);
            Assert.Equal(0, list.ExitCode);
            Assert.Equal(string.Empty, list.StandardError);
            string expectedListLine =
                $"* {layoutName}  {Formats.MonitorCount(savedLayout.Monitors.Length)}  1 icon  {Formats.ListDate(savedLayout.SavedAt, TimeZoneInfo.Local)}{Environment.NewLine}";
            Assert.Equal(expectedListLine, list.StandardOutput);

            // Verify ordered log events with strict SPEC §18 UTC timestamp, level, and complete message.
            Assert.True(File.Exists(paths.LogFile));
            string[] logLines = File.ReadAllLines(paths.LogFile);
            Assert.Equal(4, logLines.Length);
            AssertLogLine(logLines[0], "INFO", $"Saved 1 icon to \"{layoutName}\".");
            AssertLogLine(logLines[1], "INFO", $"Restored 1 icon from \"{layoutName}\".");
            AssertLogLine(logLines[2], "INFO", "Put 1 icon back.");
            AssertLogLine(logLines[3], "WARN", "There is nothing to undo.");
        }

        // Probe file must be gone after ProbeIcon.Dispose().
        Assert.False(File.Exists(probeFilePath), $"Probe file '{probeFilePath}' was not removed on disposal.");

        before.AssertUnchanged();
    }

    private static void AssertLogLine(string line, string expectedLevel, string expectedMessage)
    {
        Assert.True(line.Length > 21, $"Log line too short for SPEC §18 format: '{line}'.");
        string timestamp = line[..20];
        Assert.True(
            DateTimeOffset.TryParseExact(
                timestamp,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _),
            $"Invalid SPEC §18 UTC timestamp '{timestamp}' in log line '{line}'.");
        Assert.Equal(' ', line[20]);
        Assert.Equal($"{expectedLevel} {expectedMessage}", line[21..]);
    }

    private static IconPosition WaitForDistinctPosition(ScopedDesktopIcons scoped, IconPosition original, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        IconPosition latest = original;
        IconPosition? settledCandidate = null;
        var stableFor = new Stopwatch();
        while (sw.Elapsed < timeout)
        {
            IReadOnlyList<IconPosition> icons = scoped.ReadIcons();
            IconPosition? match = icons.FirstOrDefault(i => string.Equals(i.Id, original.Id, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                latest = match;
                if (match.X != original.X || match.Y != original.Y)
                {
                    if (settledCandidate is not null && settledCandidate.X == match.X && settledCandidate.Y == match.Y)
                    {
                        if (stableFor.Elapsed >= TimeSpan.FromMilliseconds(500))
                        {
                            return match;
                        }
                    }
                    else
                    {
                        settledCandidate = match;
                        stableFor.Restart();
                    }

                    Thread.Sleep(100);
                    continue;
                }
            }

            settledCandidate = null;
            stableFor.Reset();
            Thread.Sleep(50);
        }

        return latest;
    }

    private sealed class OwnerDataSnapshot
    {
        private readonly FolderSnapshot _roaming;
        private readonly FolderSnapshot _local;

        private OwnerDataSnapshot(FolderSnapshot roaming, FolderSnapshot local)
        {
            _roaming = roaming;
            _local = local;
        }

        public static OwnerDataSnapshot Capture()
        {
            string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berth");
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berth");
            return new OwnerDataSnapshot(FolderSnapshot.Capture(roaming), FolderSnapshot.Capture(local));
        }

        public void AssertUnchanged()
        {
            OwnerDataSnapshot after = Capture();
            _roaming.AssertEqual(after._roaming);
            _local.AssertEqual(after._local);
        }

        private sealed record FsEntry(string RelativePath, bool IsDirectory, bool IsReparsePoint, long Length, DateTime LastWriteUtc);

        private sealed class FolderSnapshot
        {
            private readonly bool _exists;
            private readonly bool _rootIsReparsePoint;
            private readonly DateTime _rootLastWriteUtc;
            private readonly List<FsEntry> _entries;

            private FolderSnapshot(bool exists, bool rootIsReparsePoint, DateTime rootLastWriteUtc, List<FsEntry> entries)
            {
                _exists = exists;
                _rootIsReparsePoint = rootIsReparsePoint;
                _rootLastWriteUtc = rootLastWriteUtc;
                _entries = entries;
            }

            public static FolderSnapshot Capture(string root)
            {
                if (!Directory.Exists(root))
                {
                    return new FolderSnapshot(exists: false, rootIsReparsePoint: false, default, []);
                }

                var rootInfo = new DirectoryInfo(root);
                bool rootIsReparse = (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0;
                var entries = new List<FsEntry>();
                var pending = new Stack<DirectoryInfo>();
                if (!rootIsReparse)
                {
                    pending.Push(rootInfo);
                }

                while (pending.Count > 0)
                {
                    DirectoryInfo current = pending.Pop();
                    foreach (FileSystemInfo item in current.EnumerateFileSystemInfos())
                    {
                        bool isReparse = (item.Attributes & FileAttributes.ReparsePoint) != 0;
                        if (item is DirectoryInfo dir)
                        {
                            entries.Add(new FsEntry(
                                Path.GetRelativePath(root, dir.FullName),
                                IsDirectory: true,
                                IsReparsePoint: isReparse,
                                Length: 0,
                                dir.LastWriteTimeUtc));
                            if (!isReparse)
                            {
                                pending.Push(dir);
                            }
                        }
                        else if (item is FileInfo file)
                        {
                            entries.Add(new FsEntry(
                                Path.GetRelativePath(root, file.FullName),
                                IsDirectory: false,
                                IsReparsePoint: isReparse,
                                file.Length,
                                file.LastWriteTimeUtc));
                        }
                    }
                }

                entries.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
                return new FolderSnapshot(exists: true, rootIsReparse, rootInfo.LastWriteTimeUtc, entries);
            }

            public void AssertEqual(FolderSnapshot other)
            {
                Assert.Equal(_exists, other._exists);
                Assert.Equal(_rootIsReparsePoint, other._rootIsReparsePoint);
                Assert.Equal(_rootLastWriteUtc, other._rootLastWriteUtc);
                Assert.Equal(_entries, other._entries);
            }
        }
    }
}
