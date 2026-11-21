using System.Windows.Forms;
using Berth.App;
using Berth.Core;
using Berth.Desktop;

namespace Berth.Tests.App;

public sealed class BalloonTests
{
    [Fact]
    public void PolicyReturnsNullForSuccessSuccessWithMissingAndNoLayout()
    {
        Assert.Null(BalloonPolicy.For(OperationResult.Restored("Laptop", total: 5, inPlace: 5, notPlaced: 0, missing: 0)));
        Assert.Null(BalloonPolicy.For(OperationResult.Restored("Laptop", total: 5, inPlace: 3, notPlaced: 0, missing: 2)));
        Assert.Null(BalloonPolicy.For(OperationResult.Refused(Refusal.NoLayout)));
    }

    [Fact]
    public void PolicyReturnsSpecTitleAndExactMessageForRefusalsFailuresAndPartialRestores()
    {
        OperationResult[] cases =
        [
            OperationResult.Refused(Refusal.AutoArrange),
            OperationResult.Refused(Refusal.DesktopUnavailable),
            OperationResult.Refused(Refusal.Busy),
            OperationResult.Failed(FailureKind.WriteFailed, path: @"C:\Data\undo.json", detail: "Access denied"),
            OperationResult.Failed(FailureKind.DesktopUnavailable, path: null, detail: "Explorer exited"),
            OperationResult.Restored("Laptop", total: 5, inPlace: 3, notPlaced: 2, missing: 0),
            OperationResult.Restored("Laptop", total: 5, inPlace: 2, notPlaced: 2, missing: 1),
        ];

        foreach (OperationResult result in cases)
        {
            BalloonMessage? balloon = BalloonPolicy.For(result);
            Assert.NotNull(balloon);
            Assert.Equal(BalloonPolicy.FailureTitle, balloon.Title);
            Assert.Equal(Messages.Refusal(result), balloon.Text);
        }

        // Verify the SPEC §8 partial sentence wording explicitly for NotPlaced.
        BalloonMessage partial = BalloonPolicy.For(
            OperationResult.Restored("Laptop", total: 5, inPlace: 3, notPlaced: 2, missing: 0))!;
        Assert.Equal("Restored 3 of 5 icons. 2 did not stay where they were saved.", partial.Text);
    }

    [Fact]
    public void PolicyShortensOverlongMessagesWithin255CharactersPreservingClosingGuidance()
    {
        string longException = new string('E', 400);
        BalloonMessage unexpected = BalloonPolicy.For(
            OperationResult.Failed(FailureKind.Unexpected, path: null, detail: longException))!;
        Assert.True(unexpected.Text.Length <= BalloonPolicy.MaxBodyLength);
        Assert.EndsWith(". The details are in the log.", unexpected.Text, StringComparison.Ordinal);
        Assert.Contains('\u2026', unexpected.Text);

        string longPath = @"C:\Users\Test\" + new string('D', 300) + @"\undo.json";
        BalloonMessage writeFailed = BalloonPolicy.For(
            OperationResult.Failed(FailureKind.WriteFailed, path: longPath, detail: "Access denied"))!;
        Assert.True(writeFailed.Text.Length <= BalloonPolicy.MaxBodyLength);
        Assert.EndsWith("\u2026", writeFailed.Text, StringComparison.Ordinal);
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class TrayBalloonTests(AppFixture appFixture)
{
    [Fact]
    public void ShowBalloonDropsQueuedCallbackWhenDisposedBeforeUiRuns()
    {
        var theme = new TaskbarTheme(@"Software\Berth.Tests\BalloonRaceTheme", () => 0);
        Action? queued = null;
        int showCount = 0;

        appFixture.Invoke(() =>
        {
            using var icon = new TrayIcon(
                theme,
                marshal: action => queued = action,
                showBalloon: (_, _, _) => showCount++);

            icon.ShowBalloon(BalloonPolicy.FailureTitle, "Auto arrange icons is on.");
            Assert.NotNull(queued);

            // Dispose BEFORE the marshalled UI callback runs -> queued callback must observe _disposed and do nothing!
            icon.Dispose();
            queued!();
        });

        Assert.Equal(0, showCount);
    }

    [Fact]
    public void TrayAppShowsOneWarningBalloonOnAutoArrangeAndPartialFailureSilentOnSuccessAndOpensWindowOnClick()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "auto-restore-balloon-report.txt");
        string instanceName = $"test-balloon-{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.AutoRestoreBalloonScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Isolated child exited {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

        Assert.Equal(string.Empty, report["workerError"]);
        Assert.Equal("1", report["balloonsAfterRefusal"]);
        Assert.Equal("2", report["balloonsAfterPartial"]);
        Assert.Equal("2", report["balloonsAfterSuccess"]);
        Assert.Equal(BalloonPolicy.FailureTitle, report["refusalTitle"]);
        Assert.Equal(
            "Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again.",
            report["refusalText"]);
        Assert.Equal(
            "Restored 1 of 2 icons. 1 did not stay where it was saved.",
            report["partialText"]);
        Assert.Equal("True", report["allOnUiThread"]);
        Assert.Equal("True", report["allWarningIcon"]);
        Assert.Equal("1", report["bringToFrontCount"]);

        // Exactly 1 log line per automatic restore (3 restores -> 3 lines: WARN, FAIL, INFO).
        Assert.Equal("3", report["logLineCount"]);
        Assert.EndsWith(
            " WARN Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again. (automatic, display change)",
            report["logLine0"],
            StringComparison.Ordinal);
        Assert.Contains(" FAIL ", report["logLine1"], StringComparison.Ordinal);
        Assert.EndsWith(" (automatic, display change)", report["logLine1"], StringComparison.Ordinal);
        Assert.Contains(" INFO ", report["logLine2"], StringComparison.Ordinal);
        Assert.EndsWith(" (automatic, display change)", report["logLine2"], StringComparison.Ordinal);
    }
}
