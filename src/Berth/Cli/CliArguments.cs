using Berth.Core;

namespace Berth.Cli;

/// <summary>The command selected on Berth's command line (SPEC §13), or <see cref="None"/> when none was selected.</summary>
internal enum CliCommand
{
    None,
    Save,
    Restore,
    Undo,
    List,
    Help,
    Version,
}

/// <summary>
/// Result of parsing command-line arguments: <c>(None, null, null)</c> means no arguments were given (start the tray
/// app), whereas <c>(None, null, error)</c> means wrong usage (exit 2, SPEC §13, §17).
/// </summary>
internal sealed record CliArguments(CliCommand Command, string? Name, string? Error)
{
    private const string NameOption = "--name";
    private const string NameOptionPrefix = "--name=";

    public static CliArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            return new(CliCommand.None, null, null);
        }

        string first = args[0];
        return first switch
        {
            "save" => ParseSave(args),
            "restore" => ParseNoOptionCommand(CliCommand.Restore, args),
            "undo" => ParseNoOptionCommand(CliCommand.Undo, args),
            "list" => ParseNoOptionCommand(CliCommand.List, args),
            "--help" or "-h" or "/?" => ParseNoOptionCommand(CliCommand.Help, args),
            "--version" => ParseNoOptionCommand(CliCommand.Version, args),
            _ when first.StartsWith('-') => Invalid($"unknown option \"{Messages.SingleLine(first)}\""),
            _ => Invalid($"unknown command \"{Messages.SingleLine(first)}\""),
        };
    }

    private static CliArguments ParseSave(string[] args)
    {
        string? name = null;
        bool hasName = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == NameOption)
            {
                if (hasName)
                {
                    return Invalid("--name may be given only once");
                }

                if (i + 1 >= args.Length)
                {
                    return Invalid("--name needs a value");
                }

                name = args[++i];
                hasName = true;
            }
            else if (arg.StartsWith(NameOptionPrefix, StringComparison.Ordinal))
            {
                if (hasName)
                {
                    return Invalid("--name may be given only once");
                }

                name = arg[NameOptionPrefix.Length..];
                hasName = true;
            }
            else if (arg.StartsWith('-'))
            {
                return Invalid($"unknown option \"{Messages.SingleLine(arg)}\"");
            }
            else
            {
                return Invalid($"unexpected argument \"{Messages.SingleLine(arg)}\"");
            }
        }

        return new(CliCommand.Save, name, null);
    }

    private static CliArguments ParseNoOptionCommand(CliCommand command, string[] args)
    {
        if (args.Length == 1)
        {
            return new(command, null, null);
        }

        string extra = args[1];
        if (extra == NameOption || extra.StartsWith(NameOptionPrefix, StringComparison.Ordinal))
        {
            return Invalid("--name is only valid with save");
        }

        if (extra.StartsWith('-'))
        {
            return Invalid($"unknown option \"{Messages.SingleLine(extra)}\"");
        }

        return Invalid($"unexpected argument \"{Messages.SingleLine(extra)}\"");
    }

    private static CliArguments Invalid(string error) => new(CliCommand.None, null, error);
}
