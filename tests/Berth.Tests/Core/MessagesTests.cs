using System.Globalization;
using Berth.Core;

namespace Berth.Tests.Core;

public sealed class MessagesTests
{
    private static readonly TimeZoneInfo ZonePlus2 =
        TimeZoneInfo.CreateCustomTimeZone("TestPlus2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private static readonly TimeZoneInfo ZoneMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("TestMinus5", TimeSpan.FromHours(-5), "Test-5", "Test-5");

    // 2026-11-15T07:14:03Z == 2026-11-15 09:14 in UTC+2
    private static readonly DateTimeOffset SpecSavedAt = new(2026, 11, 15, 7, 14, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset SameDayNow = new(2026, 11, 15, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextDayNow = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterSameYearNow = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextYearNow = new(2027, 1, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WorkingConstantMatchesSpec()
    {
        Assert.Equal("Working", Messages.Working);
        Assert.Equal("This monitor arrangement has no saved layout yet.", Messages.Unsaved);
        Assert.Equal("The settings file settings.json could not be read. It was left untouched.", Messages.UnreadableSettings);
        Assert.Equal(
            "Windows did not turn auto arrange off. Turn it off from the desktop: right-click, View, Auto arrange icons.",
            Messages.AutoArrangeWouldNotTurnOff);
    }

    [Fact]
    public void StatusSavedFormatsCountsAndPreviousSaveClause()
    {
        OperationResult first = OperationResult.Saved("Docked", 42, replacedEarlierSave: false);
        OperationResult replaced = OperationResult.Saved("Docked", 42, replacedEarlierSave: true);
        OperationResult single = OperationResult.Saved("Docked", 1, replacedEarlierSave: false);
        OperationResult singleReplaced = OperationResult.Saved("Docked", 1, replacedEarlierSave: true);
        OperationResult empty = OperationResult.Saved("Docked", 0, replacedEarlierSave: false);

        Assert.Equal("Saved 42 icons.", Messages.Status(first));
        Assert.Equal("Saved 42 icons.", Messages.Status(first, SameDayNow, ZonePlus2));
        Assert.Equal("Saved 42 icons. The previous save is kept.", Messages.Status(replaced));
        Assert.Equal("Saved 1 icon.", Messages.Status(single));
        Assert.Equal("Saved 1 icon. The previous save is kept.", Messages.Status(singleReplaced));
        Assert.Equal("Saved 0 icons.", Messages.Status(empty));
    }

    [Fact]
    public void StatusAndCliRestoredMatchEverySpecCaseAndOrder()
    {
        OperationResult full = OperationResult.Restored("Docked", 42, 42, 0, 0);
        OperationResult single = OperationResult.Restored("Docked", 1, 1, 0, 0);
        OperationResult zero = OperationResult.Restored("Docked", 0, 0, 0, 0);
        OperationResult missingTwo = OperationResult.Restored("Docked", 42, 40, 0, 2);
        OperationResult missingOne = OperationResult.Restored("2 monitors", 3, 2, 0, 1);
        OperationResult notPlacedTwo = OperationResult.Restored("Docked", 42, 40, 2, 0);
        OperationResult notPlacedOne = OperationResult.Restored("Docked", 3, 2, 1, 0);
        OperationResult bothPlural = OperationResult.Restored("Docked", 42, 38, 2, 2);
        OperationResult bothSingular = OperationResult.Restored("Docked", 3, 1, 1, 1);

        Assert.Equal("Restored 42 icons.", Messages.Status(full));
        Assert.Equal("Restored 42 icons from \"Docked\".", Messages.Cli(full));
        Assert.Equal("Restored 42 icons from \"Docked\".", Messages.Log(full));

        Assert.Equal("Restored 1 icon.", Messages.Status(single));
        Assert.Equal("Restored 1 icon from \"Docked\".", Messages.Cli(single));
        Assert.Equal("Restored 0 icons.", Messages.Status(zero));
        Assert.Equal("Restored 0 icons from \"Docked\".", Messages.Cli(zero));

        Assert.Equal("Restored 40 of 42 icons. 2 are no longer on the desktop.", Messages.Status(missingTwo));
        Assert.Equal("Restored 40 of 42 icons from \"Docked\". 2 are no longer on the desktop.", Messages.Cli(missingTwo));

        Assert.Equal("Restored 2 of 3 icons. 1 is no longer on the desktop.", Messages.Status(missingOne));
        Assert.Equal("Restored 2 of 3 icons from \"2 monitors\". 1 is no longer on the desktop.", Messages.Cli(missingOne));

        Assert.Equal("Restored 40 of 42 icons. 2 did not stay where they were saved.", Messages.Status(notPlacedTwo));
        Assert.Equal("Restored 40 of 42 icons. 2 did not stay where they were saved.", Messages.Refusal(notPlacedTwo));
        Assert.Equal("Restored 40 of 42 icons from \"Docked\". 2 did not stay where they were saved.", Messages.Cli(notPlacedTwo));

        Assert.Equal("Restored 2 of 3 icons. 1 did not stay where it was saved.", Messages.Status(notPlacedOne));
        Assert.Equal("Restored 2 of 3 icons from \"Docked\". 1 did not stay where it was saved.", Messages.Cli(notPlacedOne));

        Assert.Equal(
            "Restored 38 of 42 icons. 2 did not stay where they were saved. 2 are no longer on the desktop.",
            Messages.Status(bothPlural));
        Assert.Equal(
            "Restored 38 of 42 icons. 2 did not stay where they were saved. 2 are no longer on the desktop.",
            Messages.Refusal(bothPlural));
        Assert.Equal(
            "Restored 38 of 42 icons from \"Docked\". 2 did not stay where they were saved. 2 are no longer on the desktop.",
            Messages.Cli(bothPlural));

        Assert.Equal(
            "Restored 1 of 3 icons. 1 did not stay where it was saved. 1 is no longer on the desktop.",
            Messages.Status(bothSingular));
        Assert.Equal(
            "Restored 1 of 3 icons from \"Docked\". 1 did not stay where it was saved. 1 is no longer on the desktop.",
            Messages.Cli(bothSingular));
    }

    [Fact]
    public void StatusCliAndLogUndoneMatchFullAndPartialRulesWithoutLayoutName()
    {
        OperationResult full = OperationResult.Undone(42, 42, 0, 0);
        OperationResult single = OperationResult.Undone(1, 1, 0, 0);
        OperationResult zero = OperationResult.Undone(0, 0, 0, 0);
        OperationResult notPlacedTwo = OperationResult.Undone(42, 40, 2, 0);
        OperationResult notPlacedOne = OperationResult.Undone(3, 2, 1, 0);
        OperationResult missingOne = OperationResult.Undone(3, 2, 0, 1);
        OperationResult missingTwo = OperationResult.Undone(42, 40, 0, 2);
        OperationResult bothOne = OperationResult.Undone(3, 1, 1, 1);
        OperationResult bothPlural = OperationResult.Undone(5, 1, 2, 2);

        Assert.Equal("Put 42 icons back where they were before the restore.", Messages.Status(full));
        Assert.Equal("Put 42 icons back.", Messages.Cli(full));
        Assert.Equal("Put 42 icons back.", Messages.Log(full));

        Assert.Equal("Put 1 icon back where it was before the restore.", Messages.Status(single));
        Assert.Equal("Put 1 icon back.", Messages.Cli(single));
        Assert.Equal("Put 0 icons back where they were before the restore.", Messages.Status(zero));
        Assert.Equal("Put 0 icons back.", Messages.Cli(zero));

        Assert.Equal("Put 40 of 42 icons back. 2 did not stay where they were.", Messages.Status(notPlacedTwo));
        Assert.Equal("Put 40 of 42 icons back. 2 did not stay where they were.", Messages.Refusal(notPlacedTwo));
        Assert.Equal("Put 40 of 42 icons back. 2 did not stay where they were.", Messages.Cli(notPlacedTwo));
        Assert.Equal("Put 40 of 42 icons back. 2 did not stay where they were.", Messages.Log(notPlacedTwo));

        Assert.Equal("Put 2 of 3 icons back. 1 did not stay where it was.", Messages.Status(notPlacedOne));
        Assert.Equal("Put 2 of 3 icons back. 1 did not stay where it was.", Messages.Cli(notPlacedOne));

        Assert.Equal("Put 2 of 3 icons back. 1 is no longer on the desktop.", Messages.Status(missingOne));
        Assert.Equal("Put 2 of 3 icons back. 1 is no longer on the desktop.", Messages.Cli(missingOne));
        Assert.Equal("Put 2 of 3 icons back. 1 is no longer on the desktop.", Messages.Log(missingOne));

        Assert.Equal("Put 40 of 42 icons back. 2 are no longer on the desktop.", Messages.Status(missingTwo));
        Assert.Equal("Put 40 of 42 icons back. 2 are no longer on the desktop.", Messages.Cli(missingTwo));

        Assert.Equal("Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.", Messages.Status(bothOne));
        Assert.Equal("Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.", Messages.Refusal(bothOne));
        Assert.Equal("Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.", Messages.Cli(bothOne));
        Assert.Equal("Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.", Messages.Log(bothOne));

        Assert.Equal("Put 1 of 5 icons back. 2 did not stay where they were. 2 are no longer on the desktop.", Messages.Status(bothPlural));
        Assert.Equal("Put 1 of 5 icons back. 2 did not stay where they were. 2 are no longer on the desktop.", Messages.Cli(bothPlural));
        Assert.Equal("Put 1 of 5 icons back. 2 did not stay where they were. 2 are no longer on the desktop.", Messages.Log(bothPlural));
    }

    [Fact]
    public void RevertedRenamedAndDeletedFormatForStatusAndLogAndRejectCli()
    {
        var saved = new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.Zero);
        OperationResult reverted = OperationResult.Reverted("Docked", saved);
        OperationResult renamed = OperationResult.Renamed("Docked");
        OperationResult deleted = OperationResult.Deleted("Docked");

        Assert.Equal("Reverted to the save from 12 November.", Messages.Status(reverted, LaterSameYearNow, ZonePlus2));
        Assert.Equal("Reverted to the save from 12 November 2026.", Messages.Status(reverted, NextYearNow, ZonePlus2));
        Assert.Equal("Reverted to the save from today, 09:00.", Messages.Status(reverted, new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero), ZonePlus2));
        Assert.Equal("Reverted to the save from yesterday, 02:00.", Messages.Status(reverted, new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero), ZoneMinus5));

        Assert.Equal("Reverted \"Docked\" to the save from 2026-11-12T07:00:00Z.", Messages.Log(reverted));
        Assert.Equal("Renamed layout to \"Docked\".", Messages.Log(renamed));
        Assert.Equal("Deleted layout \"Docked\".", Messages.Log(deleted));

        Assert.Throws<ArgumentException>(() => Messages.Status(reverted));
        Assert.Throws<ArgumentException>(() => Messages.Cli(reverted));
        Assert.Throws<ArgumentException>(() => Messages.Status(renamed));
        Assert.Throws<ArgumentException>(() => Messages.Status(renamed, SameDayNow, ZonePlus2));
        Assert.Throws<ArgumentException>(() => Messages.Cli(renamed));
        Assert.Throws<ArgumentException>(() => Messages.Status(deleted));
        Assert.Throws<ArgumentException>(() => Messages.Status(deleted, SameDayNow, ZonePlus2));
        Assert.Throws<ArgumentException>(() => Messages.Cli(deleted));
    }

    [Fact]
    public void ComparisonUnsavedAndOtherArrangementMatchEverySpecCase()
    {
        Assert.Equal("Every icon is at its saved position.", Messages.Comparison(0, 0));
        Assert.Equal("3 icons have moved and 2 are no longer on the desktop.", Messages.Comparison(3, 2));
        Assert.Equal("3 icons have moved since this layout was saved.", Messages.Comparison(3, 0));
        Assert.Equal("2 saved icons are no longer on the desktop.", Messages.Comparison(0, 2));
        Assert.Equal("1 icon has moved since this layout was saved.", Messages.Comparison(1, 0));
        Assert.Equal("1 saved icon is no longer on the desktop.", Messages.Comparison(0, 1));
        Assert.Equal("1 icon has moved and 1 is no longer on the desktop.", Messages.Comparison(1, 1));
        Assert.Equal("1 icon has moved and 2 are no longer on the desktop.", Messages.Comparison(1, 2));
        Assert.Equal("3 icons have moved and 1 is no longer on the desktop.", Messages.Comparison(3, 1));

        Assert.Equal("Saved today, 09:14 for another monitor arrangement.", Messages.OtherArrangement(SpecSavedAt, SameDayNow, ZonePlus2));
        Assert.Equal("Saved yesterday, 02:14 for another monitor arrangement.", Messages.OtherArrangement(SpecSavedAt, NextDayNow, ZoneMinus5));
        Assert.Equal("Saved 15 November for another monitor arrangement.", Messages.OtherArrangement(SpecSavedAt, LaterSameYearNow, ZonePlus2));
        Assert.Equal("Saved 15 November 2026 for another monitor arrangement.", Messages.OtherArrangement(SpecSavedAt, NextYearNow, ZonePlus2));
    }

    [Fact]
    public void RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10()
    {
        Assert.Equal(
            "Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again.",
            Messages.Refusal(OperationResult.Refused(Refusal.AutoArrange)));
        Assert.Equal(
            "This monitor arrangement has no saved layout.",
            Messages.Refusal(OperationResult.Refused(Refusal.NoLayout)));
        Assert.Equal(
            "This layout has no previous save.",
            Messages.Refusal(OperationResult.Refused(Refusal.NoPreviousSave)));
        Assert.Equal(
            "Berth cannot reach the desktop. Explorer may be restarting.",
            Messages.Refusal(OperationResult.Refused(Refusal.DesktopUnavailable)));
        Assert.Equal(
            "There is nothing to undo.",
            Messages.Refusal(OperationResult.Refused(Refusal.NothingToUndo)));
        Assert.Equal(
            "Berth is busy with another save or restore. Try again in a moment.",
            Messages.Refusal(OperationResult.Refused(Refusal.Busy)));

        Assert.Equal("Enter a name.", Messages.Refusal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.Empty)));
        Assert.Equal("A name can be at most 40 characters.", Messages.Refusal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.TooLong)));
        Assert.Equal("Another layout already has this name.", Messages.Refusal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.Taken)));
        Assert.Equal("Enter a name.", Messages.NameProblem(NameError.Empty));
        Assert.Equal("A name can be at most 40 characters.", Messages.NameProblem(NameError.TooLong));
        Assert.Equal("Another layout already has this name.", Messages.NameProblem(NameError.Taken));

        Assert.Equal(
            "The layout file c7f017905ce518e6.json could not be read. It was left untouched.",
            Messages.Refusal(OperationResult.Refused(Refusal.UnreadableLayout, path: @"C:\data\layouts\c7f017905ce518e6.json")));
        Assert.Equal(
            "The layout file c7f017905ce518e6.json could not be read. It was left untouched.",
            Messages.UnreadableLayout("c7f017905ce518e6.json"));

        Assert.Equal(
            "Berth could not write C:\\Berth\\layouts\\a.json: Access is denied.",
            Messages.Refusal(OperationResult.Failed(FailureKind.WriteFailed, @"C:\Berth\layouts\a.json", "Access is denied.")));
        Assert.Equal(
            "Berth could not write C:\\Berth\\settings.json: Disk full.",
            Messages.WriteFailed(@"C:\Berth\settings.json", "Disk full"));

        Assert.Equal(
            "Berth could not read C:\\Berth\\layouts: Access is denied.",
            Messages.Refusal(OperationResult.Failed(FailureKind.ReadFailed, @"C:\Berth\layouts", "Access is denied.")));
        Assert.Equal(
            "Berth could not read C:\\Berth\\layouts: Cannot create folder.",
            Messages.ReadFailed(@"C:\Berth\layouts", "Cannot create folder."));

        Assert.Equal(
            "The desktop could not be reached while the icons were being placed. Undo puts back what was there.",
            Messages.Refusal(OperationResult.Failed(FailureKind.DesktopUnavailable, path: null, "ignored", layoutName: "Docked")));
        Assert.Equal(
            "The desktop could not be reached while the icons were being placed.",
            Messages.Refusal(OperationResult.Failed(FailureKind.DesktopUnavailable, path: null, "ignored", layoutName: null)));

        Assert.Equal(
            "The displays changed before any icon was moved.",
            Messages.Refusal(OperationResult.Failed(FailureKind.DisplaysChanged, path: null, "ignored", layoutName: "Docked")));
        Assert.Equal(
            "The displays changed before the undo could be checked.",
            Messages.Refusal(OperationResult.Failed(FailureKind.DisplaysChanged, path: null, "ignored", layoutName: null)));

        Assert.Equal(
            "Berth could not change the Start with Windows setting: Registry access denied.",
            Messages.StartWithWindowsFailed("Registry access denied."));

        Assert.Equal(
            "Something went wrong: The handle is invalid. The details are in the log.",
            Messages.Refusal(OperationResult.Failed(FailureKind.Unexpected, path: null, "The handle is invalid.")));
        Assert.Equal(
            "Something went wrong: Object state broken. The details are in the log.",
            Messages.Unexpected("Object state broken"));

        // Non-reverted Status and Cli agree with Refusal for refused/failed results without NotPlaced.
        OperationResult busy = OperationResult.Refused(Refusal.Busy);
        Assert.Equal(Messages.Refusal(busy), Messages.Status(busy));
        Assert.Equal(Messages.Refusal(busy), Messages.Cli(busy));
    }

    [Fact]
    public void LogAppendsOriginAcrossSuccessRefusalAndFailure()
    {
        const string Origin = "automatic, display change";
        OperationResult restored = OperationResult.Restored("2 monitors", 3, 3, 0, 0, Origin);
        OperationResult restoredWithMissing = OperationResult.Restored("2 monitors", 3, 2, 0, 1, Origin);
        OperationResult refused = OperationResult.Refused(Refusal.AutoArrange) with { Origin = Origin };
        OperationResult notPlaced = OperationResult.Restored("2 monitors", 3, 1, 2, 0, Origin);
        OperationResult lost = OperationResult.Failed(FailureKind.DesktopUnavailable, path: null, "lost", layoutName: "2 monitors") with { Origin = Origin };

        Assert.Equal("Restored 3 icons from \"2 monitors\". (automatic, display change)", Messages.Log(restored));
        Assert.Equal(
            "Restored 2 of 3 icons from \"2 monitors\". 1 is no longer on the desktop. (automatic, display change)",
            Messages.Log(restoredWithMissing));
        Assert.Equal(
            "Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again. (automatic, display change)",
            Messages.Log(refused));
        Assert.Equal(
            "Restored 1 of 3 icons from \"2 monitors\". 2 did not stay where they were saved. (automatic, display change)",
            Messages.Log(notPlaced));
        Assert.Equal(
            "The desktop could not be reached while the icons were being placed. Undo puts back what was there. (automatic, display change)",
            Messages.Log(lost));
    }

    [Fact]
    public void MultilineNamesPathsOriginsAndReasonsCollapseLineBreaksAndTrailingPeriods()
    {
        OperationResult saved = OperationResult.Saved("Work\r\nDesk", 3, replacedEarlierSave: false);
        OperationResult savedMultiBreak = OperationResult.Saved("Work\r\n\nDesk", 3, replacedEarlierSave: false);
        OperationResult savedTwoSpaces = OperationResult.Saved("Work  Desk", 3, replacedEarlierSave: false);
        OperationResult partialRestore = OperationResult.Restored("Work\nDesk", 3, 1, 1, 1, "automatic,\r\ndisplay change");
        OperationResult renamed = OperationResult.Renamed("Work\rDesk");
        OperationResult reverted = OperationResult.Reverted("Work\r\nDesk", SpecSavedAt);
        OperationResult deleted = OperationResult.Deleted("Work\r\nDesk");

        Assert.Equal("Saved 3 icons to \"Work Desk\".", Messages.Cli(saved));
        Assert.Equal("Saved 3 icons to \"Work Desk\".", Messages.Log(saved));
        Assert.Equal("Saved 3 icons to \"Work Desk\".", Messages.Cli(savedMultiBreak));
        Assert.Equal("Saved 3 icons to \"Work  Desk\".", Messages.Cli(savedTwoSpaces));
        Assert.Equal(
            "Restored 1 of 3 icons from \"Work Desk\". 1 did not stay where it was saved. 1 is no longer on the desktop. (automatic, display change)",
            Messages.Log(partialRestore));
        Assert.Equal("Renamed layout to \"Work Desk\".", Messages.Log(renamed));
        Assert.Equal("Reverted \"Work Desk\" to the save from 2026-11-15T07:14:03Z.", Messages.Log(reverted));
        Assert.Equal("Deleted layout \"Work Desk\".", Messages.Log(deleted));

        Assert.Equal("Something went wrong: one two. The details are in the log.", Messages.Unexpected("  one\r\ntwo.. \n"));
        Assert.Equal("Something went wrong: boom. The details are in the log.", Messages.Unexpected("boom . . "));
        Assert.Equal("Something went wrong: unknown error. The details are in the log.", Messages.Unexpected("  \r\n...  "));
        Assert.Equal("Berth could not write C:\\a b\\x.json: unknown error.", Messages.WriteFailed("C:\\a\r\nb\\x.json", "\t"));
        Assert.Equal("Berth could not write p: Access denied.", Messages.WriteFailed("p", "Access denied. ."));
        Assert.Equal("Berth could not read C:\\a b\\layouts: Folder locked.", Messages.ReadFailed("C:\\a\r\nb\\layouts", "Folder\nlocked.."));
        Assert.Equal("Berth could not change the Start with Windows setting: unknown error.", Messages.StartWithWindowsFailed("   "));
    }

    [Fact]
    public void ControlCharactersInNamesPathsOriginsAndReasonsAreEscapedAsUnicodeHex()
    {
        OperationResult backspaceSaved = OperationResult.Saved("AB\bC", 1, replacedEarlierSave: false);
        OperationResult ansiRestored = OperationResult.Restored("\u001b[31mRed", 2, 2, 0, 0, "auto\tm");
        OperationResult tabNamed = OperationResult.Renamed("Tab\tName");
        OperationResult highControls = OperationResult.Deleted("Del\u007fC1\u009b");
        OperationResult nullByteSaved = OperationResult.Saved("Zero\0Byte", 1, false);

        Assert.Equal("Saved 1 icon to \"AB\\u0008C\".", Messages.Cli(backspaceSaved));
        Assert.Equal("Saved 1 icon to \"AB\\u0008C\".", Messages.Log(backspaceSaved));
        Assert.Equal("Restored 2 icons from \"\\u001b[31mRed\".", Messages.Cli(ansiRestored));
        Assert.Equal("Restored 2 icons from \"\\u001b[31mRed\". (auto\\u0009m)", Messages.Log(ansiRestored));
        Assert.Equal("Renamed layout to \"Tab\\u0009Name\".", Messages.Log(tabNamed));
        Assert.Equal("Deleted layout \"Del\\u007fC1\\u009b\".", Messages.Log(highControls));
        Assert.Equal("Saved 1 icon to \"Zero\\u0000Byte\".", Messages.Cli(nullByteSaved));

        Assert.Equal(
            "The layout file bad\\u0008.json could not be read. It was left untouched.",
            Messages.UnreadableLayout(@"C:\data\layouts\bad" + "\b.json"));
        Assert.Equal(
            "Berth could not write C:\\x\\f\\u0008.json: Denied\\u001b[0m.",
            Messages.WriteFailed("C:\\x\\f\b.json", "Denied\u001b[0m."));
        Assert.Equal(
            "Something went wrong: bad\\u0007bell. The details are in the log.",
            Messages.Unexpected("bad\abell"));
    }

    [Fact]
    public void FormattingUsesInvariantNumbersAndEnglishMonthsUnderHostileCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        var hostile = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        hostile.NumberFormat.NumberGroupSeparator = " ";
        try
        {
            CultureInfo.CurrentCulture = hostile;
            CultureInfo.CurrentUICulture = hostile;

            OperationResult restored = OperationResult.Restored("Docked", 1200, 1000, 100, 100);
            OperationResult reverted = OperationResult.Reverted("Docked", SpecSavedAt);

            Assert.Equal("Saved 1200 icons.", Messages.Status(OperationResult.Saved("Docked", 1200, false)));
            Assert.Equal("Saved 1200 icons to \"Docked\".", Messages.Cli(OperationResult.Saved("Docked", 1200, false)));
            Assert.Equal("Put 1200 icons back where they were before the restore.", Messages.Status(OperationResult.Undone(1200, 1200, 0, 0)));
            Assert.Equal("Put 1200 icons back.", Messages.Cli(OperationResult.Undone(1200, 1200, 0, 0)));
            Assert.Equal(
                "Restored 1000 of 1200 icons from \"Docked\". 100 did not stay where they were saved. 100 are no longer on the desktop.",
                Messages.Cli(restored));
            Assert.Equal("Reverted to the save from 15 November.", Messages.Status(reverted, LaterSameYearNow, ZonePlus2));
            Assert.Equal("Saved 15 November 2026 for another monitor arrangement.", Messages.OtherArrangement(SpecSavedAt, NextYearNow, ZonePlus2));
            Assert.Equal("1000 icons have moved and 1000 are no longer on the desktop.", Messages.Comparison(1000, 1000));
            Assert.Equal("1000 icons have moved since this layout was saved.", Messages.Comparison(1000, 0));
            Assert.Equal("1000 saved icons are no longer on the desktop.", Messages.Comparison(0, 1000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void InvalidArgumentsAndInconsistentOperationResultsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => Messages.Status(null!));
        Assert.Throws<ArgumentNullException>(() => Messages.Status(null!, SameDayNow, ZonePlus2));
        Assert.Throws<ArgumentNullException>(() => Messages.Status(OperationResult.Saved("Docked", 1, false), SameDayNow, null!));
        Assert.Throws<ArgumentNullException>(() => Messages.Refusal(null!));
        Assert.Throws<ArgumentNullException>(() => Messages.Cli(null!));
        Assert.Throws<ArgumentNullException>(() => Messages.Log(null!));
        Assert.Throws<ArgumentNullException>(() => Messages.OtherArrangement(SpecSavedAt, SameDayNow, null!));
        Assert.Throws<ArgumentNullException>(() => Messages.Unexpected(null!));

        Assert.Throws<ArgumentOutOfRangeException>(() => Messages.Comparison(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Messages.Comparison(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Messages.Status(OperationResult.Saved("Docked", -1, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Messages.Cli(OperationResult.Saved("Docked", -1, false)));

        Assert.Throws<ArgumentException>(() => Messages.Status(OperationResult.Restored("Docked", 3, 2, 0, 0)));
        Assert.Throws<ArgumentException>(() => Messages.Status(OperationResult.Undone(3, 1, 0, 0)));
        Assert.Throws<ArgumentException>(() => Messages.Cli(OperationResult.Undone(0, int.MaxValue, int.MaxValue, 2)));
        Assert.Throws<ArgumentException>(() => Messages.Status(OperationResult.Undone(0, int.MaxValue, int.MaxValue, 2)));
        Assert.Throws<ArgumentException>(() => Messages.Status(OperationResult.Restored("Docked", 3, 1, 1, 1) with { NotPlaced = -1, InPlace = 3 }));
        Assert.Throws<ArgumentException>(() => Messages.Status(OperationResult.Undone(1, 1, 0, 0) with { LayoutName = "Docked" }));
        Assert.Throws<ArgumentException>(() => Messages.Cli(OperationResult.Undone(1, 1, 0, 0) with { LayoutName = "Docked" }));
        Assert.Throws<ArgumentException>(() => Messages.Status(OperationResult.Saved("   ", 1, false)));
        Assert.Throws<ArgumentException>(() => Messages.Log(OperationResult.Saved("Docked", 1, false) with { Origin = " \r\n " }));
        Assert.Throws<ArgumentException>(() => Messages.UnreadableLayout("   "));
        Assert.Throws<ArgumentException>(() => Messages.UnreadableLayout(@"C:\layouts\"));
        Assert.Throws<ArgumentException>(() => Messages.WriteFailed(" ", "reason"));
        Assert.Throws<ArgumentException>(() => Messages.ReadFailed(" ", "reason"));
        Assert.Throws<ArgumentException>(() => Messages.NameProblem(NameError.None));
        Assert.Throws<ArgumentException>(() => Messages.Refusal(OperationResult.Saved("Docked", 1, false)));
        Assert.Throws<ArgumentException>(() => Messages.Refusal(OperationResult.Refused(Refusal.AutoArrange) with { Refusal = Refusal.None }));
        Assert.Throws<ArgumentException>(() => Messages.Refusal(OperationResult.Failed(FailureKind.WriteFailed, "p", "d") with { Failure = FailureKind.None }));
    }
}
