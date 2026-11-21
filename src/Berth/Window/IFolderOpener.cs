using System.Diagnostics;

namespace Berth.Window;

/// <summary>
/// Opens File Explorer with a layout file selected (SPEC §9 "Open folder"); injectable so tests never launch Explorer.
/// </summary>
internal interface IFolderOpener
{
    void OpenSelectFile(string fullPath);
}

internal sealed class ShellFolderOpener : IFolderOpener
{
    internal static string BuildArguments(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        return $"/select,\"{fullPath}\"";
    }

    public void OpenSelectFile(string fullPath)
    {
        var startInfo = new ProcessStartInfo("explorer.exe", BuildArguments(fullPath))
        {
            UseShellExecute = false,
        };
        using Process? process = Process.Start(startInfo);
    }
}
