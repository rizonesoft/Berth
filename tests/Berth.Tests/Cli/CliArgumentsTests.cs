using Berth.Cli;

namespace Berth.Tests.Cli;

public sealed class CliArgumentsTests
{
    [Fact]
    public void ParseNullThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliArguments.Parse(null!));
    }

    [Fact]
    public void ParseNoArgumentsSelectsTrayStartWithNoError()
    {
        Assert.Equal(new CliArguments(CliCommand.None, null, null), CliArguments.Parse([]));
    }

    [Fact]
    public void ParseValidSingleCommandReturnsExactCommandAndNoError()
    {
        (string Arg, CliCommand Expected)[] cases =
        [
            ("save", CliCommand.Save),
            ("restore", CliCommand.Restore),
            ("undo", CliCommand.Undo),
            ("list", CliCommand.List),
            ("--help", CliCommand.Help),
            ("-h", CliCommand.Help),
            ("/?", CliCommand.Help),
            ("--version", CliCommand.Version),
        ];

        foreach ((string arg, CliCommand expected) in cases)
        {
            Assert.Equal(new CliArguments(expected, null, null), CliArguments.Parse([arg]));
        }
    }

    [Fact]
    public void ParseSaveAcceptsNameInSpaceAndEqualsFormsIncludingSpacesAndExplicitEmptyValue()
    {
        Assert.Equal(new CliArguments(CliCommand.Save, "Docked", null), CliArguments.Parse(["save", "--name", "Docked"]));
        Assert.Equal(new CliArguments(CliCommand.Save, "Two Words", null), CliArguments.Parse(["save", "--name", "Two Words"]));
        Assert.Equal(new CliArguments(CliCommand.Save, "Docked", null), CliArguments.Parse(["save", "--name=Docked"]));
        Assert.Equal(new CliArguments(CliCommand.Save, "Two Words", null), CliArguments.Parse(["save", "--name=Two Words"]));

        // Explicitly supplied empty values are preserved as "" (not null) so §10 name validation produces "Enter a name.".
        Assert.Equal(new CliArguments(CliCommand.Save, "", null), CliArguments.Parse(["save", "--name", ""]));
        Assert.Equal(new CliArguments(CliCommand.Save, "", null), CliArguments.Parse(["save", "--name="]));

        // An option-looking token immediately after --name is consumed verbatim as the layout name.
        Assert.Equal(new CliArguments(CliCommand.Save, "--name", null), CliArguments.Parse(["save", "--name", "--name"]));
    }

    [Theory]
    [InlineData("frobnicate", "unknown command \"frobnicate\"")]
    [InlineData("", "unknown command \"\"")]
    [InlineData("Save", "unknown command \"Save\"")]
    [InlineData("RESTORE", "unknown command \"RESTORE\"")]
    [InlineData("rest", "unknown command \"rest\"")]
    [InlineData("lis", "unknown command \"lis\"")]
    [InlineData("--x", "unknown option \"--x\"")]
    [InlineData("-x", "unknown option \"-x\"")]
    public void ParseRejectsUnknownCommandsWrongCasePrefixesEmptyFirstArgAndUnknownFirstArgOptions(string first, string expectedError)
    {
        Assert.Equal(new CliArguments(CliCommand.None, null, expectedError), CliArguments.Parse([first]));
    }

    [Fact]
    public void ParseRejectsBadOptionsMissingOrDuplicateNameAndExtraArguments()
    {
        // Missing value for --name
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name needs a value"),
            CliArguments.Parse(["save", "--name"]));

        // Second --name consumed as value of first --name, leaving trailing positional argument
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"Docked\""),
            CliArguments.Parse(["save", "--name", "--name", "Docked"]));

        // Duplicate --name (space and equals combinations)
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name may be given only once"),
            CliArguments.Parse(["save", "--name", "A", "--name", "B"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name may be given only once"),
            CliArguments.Parse(["save", "--name=A", "--name=B"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name may be given only once"),
            CliArguments.Parse(["save", "--name", "A", "--name=B"]));

        // Unknown option after a known command
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unknown option \"--x\""),
            CliArguments.Parse(["save", "--x"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unknown option \"-x\""),
            CliArguments.Parse(["list", "-x"]));

        // --name on commands other than save
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name is only valid with save"),
            CliArguments.Parse(["restore", "--name", "Docked"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name is only valid with save"),
            CliArguments.Parse(["undo", "--name=Docked"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name is only valid with save"),
            CliArguments.Parse(["list", "--name", "Docked"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name is only valid with save"),
            CliArguments.Parse(["--help", "--name", "Docked"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "--name is only valid with save"),
            CliArguments.Parse(["--version", "--name=Docked"]));

        // Extra positional argument after a complete command
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["save", "extra"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["save", "--name", "Docked", "extra"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["restore", "extra"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["undo", "extra"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["list", "extra"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["--help", "extra"]));
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unexpected argument \"extra\""),
            CliArguments.Parse(["--version", "extra"]));

        // Control characters / newlines in unknown command are sanitized to one line per SPEC §13.
        Assert.Equal(
            new CliArguments(CliCommand.None, null, "unknown command \"bad arg\""),
            CliArguments.Parse(["bad\r\narg"]));
    }
}
