using System.Text;

namespace Berth.Cli;

/// <summary>Help text printed by <c>berth --help</c>, <c>-h</c>, and <c>/?</c> (SPEC §13, §17).</summary>
internal static class Usage
{
    public static string Text { get; } = BuildText();

    private static string BuildText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Berth saves the positions of your desktop icons and puts them back.");
        sb.AppendLine();
        sb.AppendLine("Usage:");
        sb.AppendLine("  berth                       Starts the tray app (opens the window on first run)");
        sb.AppendLine("  berth save                  Saves the desktop to the current arrangement's layout");
        sb.AppendLine("  berth save --name <name>    Saves and names (or renames) the current arrangement's layout");
        sb.AppendLine("  berth restore               Restores the current arrangement's layout");
        sb.AppendLine("  berth undo                  Puts icons back where they were before the last restore");
        sb.AppendLine("  berth list                  Lists the saved layouts");
        sb.AppendLine("  berth --help, -h, /?        Prints the usage text");
        sb.AppendLine("  berth --version             Prints the version");
        sb.AppendLine();
        sb.AppendLine("Exit codes:");
        sb.AppendLine("  0  Done");
        sb.AppendLine("  1  An error: a write failed, an icon did not stay where it was put, or something unexpected");
        sb.AppendLine("  2  Wrong usage: an unknown command or option, or a missing or invalid --name");
        sb.Append("  3  Refused: auto arrange on, no layout, nothing to undo, desktop unreachable, busy, or unreadable layout");
        return sb.ToString();
    }
}
