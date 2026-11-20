using System.IO;
using Berth.Core;

namespace Berth.Cli;

/// <summary>Exit codes of the command-line interface (SPEC §13).</summary>
internal static class CliExit
{
    public const int Done = 0;
    public const int Error = 1;
    public const int Usage = 2;
    public const int Refused = 3;
}

/// <summary>Runs parsed CLI commands against <see cref="LayoutService"/> and writes single-line results (SPEC §13).</summary>
internal sealed class CliRunner
{
    private readonly LayoutService _service;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CliRunner(LayoutService service, TextWriter output, TextWriter error)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public int Run(CliArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Error is not null)
        {
            return CliExit.Usage;
        }

        return args.Command switch
        {
            CliCommand.Save => WriteOperationResult(_service.Save(args.Name)),
            CliCommand.Restore => WriteOperationResult(_service.Restore()),
            CliCommand.Undo => WriteOperationResult(_service.Undo()),
            _ => CliExit.Usage,
        };
    }

    private int WriteOperationResult(OperationResult result)
    {
        string line = Messages.Cli(result);
        switch (result.Kind)
        {
            case OperationKind.Saved:
            case OperationKind.Restored:
            case OperationKind.Undone:
                _output.WriteLine(line);
                return CliExit.Done;

            case OperationKind.Refused when result.Refusal == Refusal.InvalidName:
                _error.WriteLine(line);
                return CliExit.Usage;

            case OperationKind.Refused:
                _error.WriteLine(line);
                return CliExit.Refused;

            case OperationKind.Failed:
                _error.WriteLine(line);
                return CliExit.Error;

            default:
                throw new InvalidOperationException($"Unexpected CLI operation result kind '{result.Kind}'.");
        }
    }
}
