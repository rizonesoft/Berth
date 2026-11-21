using Berth.Core;
using Berth.Core.Model;
using Berth.Window;

namespace Berth.Tests.Window;

public sealed class StatusViewModelTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
    private static readonly MonitorInfo MonA = new(0, 0, 2560, 1440, 100, true);
    private static readonly WindowOperationRequest DummySaveRequest = new(
        WindowOperationKind.Save,
        () => OperationResult.Saved("Docked", 3, false));

    [Fact]
    public void CoversEverySpec8TableRowWithExactSentenceAndRightSide()
    {
        var vm = new StatusViewModel(new FixedClock(FixedNow), TimeZoneInfo.Utc);

        // Row 1: Working
        vm.SetBusy();
        Assert.Equal("Working", vm.Sentence);
        Assert.Equal(string.Empty, vm.RightText);
        Assert.Equal(StatusAction.None, vm.Action);

        // Row 2: Restored (full + partial NotPlaced both offer Undo when canUndo is true, never Retry)
        vm.SetResult(OperationResult.Restored("Docked", 42, 42, 0, 0), canUndo: true);
        Assert.Equal("Restored 42 icons.", vm.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Action);
        Assert.Equal("Undo", vm.ActionButtonText);

        vm.SetResult(OperationResult.Restored("Docked", 42, 38, 2, 2), canUndo: true, retryRequest: DummySaveRequest);
        Assert.Equal("Restored 38 of 42 icons. 2 did not stay where they were saved. 2 are no longer on the desktop.", vm.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Action);

        // Row 3: Undone (full -> no Undo; missing-only -> no Undo; NotPlaced -> Undo again while canUndo)
        vm.SetResult(OperationResult.Undone(42, 42, 0, 0), canUndo: false);
        Assert.Equal("Put 42 icons back where they were before the restore.", vm.Sentence);
        Assert.Equal(StatusAction.None, vm.Action);

        vm.SetResult(OperationResult.Undone(3, 2, 0, 1), canUndo: false);
        Assert.Equal("Put 2 of 3 icons back. 1 is no longer on the desktop.", vm.Sentence);
        Assert.Equal(StatusAction.None, vm.Action);

        vm.SetResult(OperationResult.Undone(42, 40, 2, 0), canUndo: true, retryRequest: DummySaveRequest);
        Assert.Equal("Put 40 of 42 icons back. 2 did not stay where they were.", vm.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Action);

        // Row 4: Saved (first & over earlier save)
        vm.SetResult(OperationResult.Saved("Docked", 42, replacedEarlierSave: false));
        Assert.Equal("Saved 42 icons.", vm.Sentence);
        Assert.Equal(StatusAction.None, vm.Action);

        vm.SetResult(OperationResult.Saved("Docked", 42, replacedEarlierSave: true));
        Assert.Equal("Saved 42 icons. The previous save is kept.", vm.Sentence);
        Assert.Equal(StatusAction.None, vm.Action);

        // Row 5: Reverted
        vm.SetResult(OperationResult.Reverted("Docked", new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Reverted to the save from 12 November.", vm.Sentence);
        Assert.Equal(StatusAction.None, vm.Action);

        // Row 6: Refused / Failed (Retry offered only when retrying can help)
        vm.SetResult(OperationResult.Refused(Refusal.DesktopUnavailable), retryRequest: DummySaveRequest);
        Assert.Equal("Berth cannot reach the desktop. Explorer may be restarting.", vm.Sentence);
        Assert.Equal(StatusAction.Retry, vm.Action);
        Assert.Equal("Retry", vm.ActionButtonText);

        vm.SetResult(OperationResult.Refused(Refusal.Busy), retryRequest: DummySaveRequest);
        Assert.Equal(StatusAction.Retry, vm.Action);

        vm.SetResult(OperationResult.Failed(FailureKind.WriteFailed, "a.json", "Disk full"), retryRequest: DummySaveRequest);
        Assert.Equal(StatusAction.Retry, vm.Action);

        foreach (Refusal noRetry in new[] { Refusal.AutoArrange, Refusal.NoLayout, Refusal.NoPreviousSave, Refusal.NothingToUndo, Refusal.InvalidName })
        {
            OperationResult refused = noRetry == Refusal.InvalidName
                ? OperationResult.Refused(noRetry, nameError: NameError.Empty)
                : OperationResult.Refused(noRetry);
            vm.SetResult(refused, retryRequest: DummySaveRequest);
            Assert.Equal(StatusAction.None, vm.Action);
        }

        // Rows 7-12: Comparison states (after clearing sticky via Renamed/Deleted or fresh VM)
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonA]),
            "Docked",
            new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero),
            [MonA],
            [new IconPosition("a", "A", 10, 10)],
            null);
        LayoutRow activeRow = LayoutRow.FromSaved(layout, isActive: true);
        LayoutRow otherRow = LayoutRow.FromSaved(layout, isActive: false);
        LayoutRow unsavedRow = LayoutRow.Unsaved("unsaved-key");
        var liveDesktop = new DesktopState([new IconPosition("a", "A", 10, 10)], [MonA], false, false, FixedNow);

        var cmpVm = new StatusViewModel(new FixedClock(FixedNow), TimeZoneInfo.Utc);

        // Row 7: In place
        cmpVm.SetComparison(activeRow, MakeComparison(inPlace: 2, moved: 0, missing: 0), ComparisonKind.Compared, liveDesktop);
        Assert.Equal("Every icon is at its saved position.", cmpVm.Sentence);
        Assert.Equal("Checked 09:14", cmpVm.RightText);
        Assert.True(cmpVm.ShowRightText);

        // Row 8: Moved and missing
        cmpVm.SetComparison(activeRow, MakeComparison(inPlace: 0, moved: 3, missing: 2), ComparisonKind.Compared, liveDesktop);
        Assert.Equal("3 icons have moved and 2 are no longer on the desktop.", cmpVm.Sentence);
        Assert.Equal("Checked 09:14", cmpVm.RightText);

        // Row 9: Moved only
        cmpVm.SetComparison(activeRow, MakeComparison(inPlace: 1, moved: 3, missing: 0), ComparisonKind.Compared, liveDesktop);
        Assert.Equal("3 icons have moved since this layout was saved.", cmpVm.Sentence);
        Assert.Equal("Checked 09:14", cmpVm.RightText);

        // Row 10: Missing only
        cmpVm.SetComparison(activeRow, MakeComparison(inPlace: 1, moved: 0, missing: 2), ComparisonKind.Compared, liveDesktop);
        Assert.Equal("2 saved icons are no longer on the desktop.", cmpVm.Sentence);
        Assert.Equal("Checked 09:14", cmpVm.RightText);

        // Row 11: Unsaved
        cmpVm.SetComparison(unsavedRow, MakeComparison(inPlace: 0, moved: 0, missing: 0), ComparisonKind.Unsaved, liveDesktop);
        Assert.Equal("This monitor arrangement has no saved layout yet.", cmpVm.Sentence);
        Assert.Equal(string.Empty, cmpVm.RightText);

        // Row 12: Other arrangement
        cmpVm.SetComparison(otherRow, MakeComparison(inPlace: 0, moved: 0, missing: 0), ComparisonKind.Neutral, liveDesktop);
        Assert.Equal("Saved 12 November for another monitor arrangement.", cmpVm.Sentence);
        Assert.Equal(string.Empty, cmpVm.RightText);

        // Renamed / Deleted clear Working and fall through to refreshed comparison without throwing.
        cmpVm.SetBusy();
        Assert.Equal("Working", cmpVm.Sentence);
        cmpVm.SetResult(OperationResult.Renamed("Docked"));
        Assert.Equal("Saved 12 November for another monitor arrangement.", cmpVm.Sentence);
        cmpVm.SetBusy();
        cmpVm.SetResult(OperationResult.Deleted("Docked"));
        Assert.Equal("Saved 12 November for another monitor arrangement.", cmpVm.Sentence);
    }

    [Fact]
    public void KeepsStickySentenceOnUnchangedRefreshAndClearsOnBusySelectionOrChangedComparison()
    {
        var vm = new StatusViewModel(new FixedClock(FixedNow), TimeZoneInfo.Utc);
        var layoutA = new Layout(Layout.CurrentSchemaVersion, "key-a", "Docked", FixedNow, [MonA], [new IconPosition("a", "A", 10, 10), new IconPosition("b", "B", 20, 20)], null);
        var layoutB = new Layout(Layout.CurrentSchemaVersion, "key-b", "Laptop", FixedNow, [MonA], [new IconPosition("a", "A", 10, 10)], null);
        LayoutRow rowA = LayoutRow.FromSaved(layoutA, isActive: true);
        LayoutRow rowB = LayoutRow.FromSaved(layoutB, isActive: false);

        var desktop1 = new DesktopState([new IconPosition("a", "A", 99, 99), new IconPosition("b", "B", 20, 20)], [MonA], false, false, FixedNow);
        LayoutComparison cmp1 = LayoutComparison.Compare(layoutA, desktop1.Icons);
        vm.SetComparison(rowA, cmp1, ComparisonKind.Compared, desktop1, canUndo: true);

        vm.SetResult(OperationResult.Restored("Docked", 2, 2, 0, 0), canUndo: true);
        Assert.Equal("Restored 2 icons.", vm.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Action);

        // 1. Unchanged refresh with a LATER CheckedAt timestamp MUST keep the sticky sentence!
        var desktopLater = desktop1 with { CheckedAt = FixedNow.AddMinutes(1) };
        LayoutComparison cmpRebuilt = LayoutComparison.Compare(layoutA, desktopLater.Icons);
        vm.SetComparison(rowA, cmpRebuilt, ComparisonKind.Compared, desktopLater, canUndo: true);
        Assert.Equal("Restored 2 icons.", vm.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Action);

        // 2. Unchanged refresh where another Berth process consumed undo.json drops Undo button while keeping sentence!
        vm.SetComparison(rowA, cmpRebuilt, ComparisonKind.Compared, desktopLater, canUndo: false);
        Assert.Equal("Restored 2 icons.", vm.Sentence);
        Assert.Equal(StatusAction.None, vm.Action);

        // 3. Refresh where a DIFFERENT icon moved (identical counts: 1 in place, 1 moved!) MUST clear sticky sentence!
        var desktopDifferentMoved = new DesktopState([new IconPosition("a", "A", 10, 10), new IconPosition("b", "B", 88, 88)], [MonA], false, false, FixedNow.AddMinutes(2));
        LayoutComparison cmpDifferent = LayoutComparison.Compare(layoutA, desktopDifferentMoved.Icons);
        vm.SetComparison(rowA, cmpDifferent, ComparisonKind.Compared, desktopDifferentMoved, canUndo: false);
        Assert.Equal("1 icon has moved since this layout was saved.", vm.Sentence);
        Assert.Equal("Checked 09:16", vm.RightText);

        // 4. Selection change clears sticky sentence.
        vm.SetResult(OperationResult.Saved("Docked", 2, false));
        Assert.Equal("Saved 2 icons.", vm.Sentence);
        vm.SetComparison(rowB, LayoutComparison.Neutral(layoutB), ComparisonKind.Neutral, desktopLater);
        Assert.Equal("Saved today, 09:14 for another monitor arrangement.", vm.Sentence);

        // 5. Next operation (SetBusy) clears sticky sentence.
        vm.SetResult(OperationResult.Saved("Docked", 2, false));
        vm.SetBusy();
        Assert.Equal("Working", vm.Sentence);
    }

    [Fact]
    public void MidPlacementDesktopLossOffersUndoNotRetryAndClearingActionNotifiesShowRightText()
    {
        var vm = new StatusViewModel(new FixedClock(FixedNow), TimeZoneInfo.Utc);
        var retryReq = new WindowOperationRequest(WindowOperationKind.Restore, () => OperationResult.Restored("Docked", 2, 2, 0, 0));

        OperationResult lostDuringRestore = OperationResult.Failed(
            FailureKind.DesktopUnavailable,
            path: null,
            Messages.DesktopLostDuringRestore,
            layoutName: "Docked");
        Assert.False(StatusViewModel.OffersRetry(lostDuringRestore));

        vm.SetResult(lostDuringRestore, canUndo: true, retryRequest: retryReq);
        Assert.Equal(Messages.DesktopLostDuringRestore, vm.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Action);
        Assert.False(vm.ShowRightText);

        // Moving an icon clears Undo and MUST raise PropertyChanged(ShowRightText) so Checked time reappears.
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonA]),
            "Docked",
            FixedNow,
            [MonA],
            [new IconPosition("a", "A", 10, 10)],
            null);
        LayoutRow row = LayoutRow.FromSaved(layout, isActive: true);
        var desktop1 = new DesktopState([new IconPosition("a", "A", 10, 10)], [MonA], false, false, FixedNow);
        vm.SetComparison(row, LayoutComparison.Compare(layout, desktop1.Icons), ComparisonKind.Compared, desktop1, canUndo: true);
        vm.SetResult(OperationResult.Restored("Docked", 1, 1, 0, 0), canUndo: true);
        Assert.Equal(StatusAction.Undo, vm.Action);
        Assert.False(vm.ShowRightText);

        var changedProps = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        var desktop2 = new DesktopState([new IconPosition("a", "A", 99, 99)], [MonA], false, false, FixedNow.AddMinutes(1));
        vm.SetComparison(row, LayoutComparison.Compare(layout, desktop2.Icons), ComparisonKind.Compared, desktop2, canUndo: true);

        Assert.Equal(StatusAction.None, vm.Action);
        Assert.True(vm.ShowRightText);
        Assert.Equal("Checked 09:15", vm.RightText);
        Assert.Contains(nameof(StatusViewModel.ShowRightText), changedProps);
    }

    private static LayoutComparison MakeComparison(int inPlace, int moved, int missing)
    {
        var icons = new List<ComparedIcon>();
        for (int i = 0; i < inPlace; i++)
        {
            var p = new IconPosition($"ip-{i}", $"IP{i}", 10 + i, 10);
            icons.Add(new ComparedIcon(p, IconState.InPlace, p, 1, false));
        }

        for (int i = 0; i < moved; i++)
        {
            var s = new IconPosition($"mv-{i}", $"MV{i}", 100 + i, 10);
            var c = new IconPosition($"mv-{i}", $"MV{i}", 500 + i, 10);
            icons.Add(new ComparedIcon(s, IconState.Moved, c, 1, false));
        }

        for (int i = 0; i < missing; i++)
        {
            var s = new IconPosition($"ms-{i}", $"MS{i}", 200 + i, 10);
            icons.Add(new ComparedIcon(s, IconState.Missing, null, 1, false));
        }

        return new LayoutComparison(icons, inPlace, moved, missing);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
