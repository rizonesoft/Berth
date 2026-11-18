using System.IO;
using System.Text.RegularExpressions;

namespace Berth.Core;

/// <summary>
/// Where Berth's files live (SPEC §14): the data root <c>%APPDATA%\Berth</c> and the local root
/// <c>%LOCALAPPDATA%\Berth</c>, or both under <c>BERTH_DATA</c> when it is set, which is also what turns test mode on.
/// </summary>
internal sealed partial class BerthPaths
{
    private BerthPaths(string dataRoot, string localRoot, bool testMode)
    {
        DataRoot = dataRoot;
        LocalRoot = localRoot;
        TestMode = testMode;
    }

    public string DataRoot { get; }

    public string LocalRoot { get; }

    /// <summary>True exactly when the roots came from <c>BERTH_DATA</c>: the SPEC §14 test switches apply only then.</summary>
    public bool TestMode { get; }

    public string LayoutsFolder => Path.Combine(DataRoot, "layouts");

    public string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public string UndoFile => Path.Combine(LocalRoot, "undo.json");

    public string LogFolder => Path.Combine(LocalRoot, "logs");

    public string LogFile => Path.Combine(LogFolder, "berth.log");

    /// <summary>Both roots under <paramref name="root"/>: <c>roaming</c> and <c>local</c>. Test mode stays off.</summary>
    public static BerthPaths ForRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string full = Path.GetFullPath(root);
        return new BerthPaths(Path.Combine(full, "roaming"), Path.Combine(full, "local"), testMode: false);
    }

    /// <summary>
    /// The roots from <c>BERTH_DATA</c> when it holds a folder (not empty, not white space), with test mode on, and
    /// the user's own <c>%APPDATA%\Berth</c> and <c>%LOCALAPPDATA%\Berth</c> otherwise. Tests pass
    /// <paramref name="getenv"/> instead of changing the process environment.
    /// </summary>
    public static BerthPaths FromEnvironment(Func<string, string?>? getenv = null)
    {
        string? data = (getenv ?? Environment.GetEnvironmentVariable)("BERTH_DATA");
        if (!string.IsNullOrWhiteSpace(data))
        {
            BerthPaths redirected = ForRoot(data);
            return new BerthPaths(redirected.DataRoot, redirected.LocalRoot, testMode: true);
        }

        return new BerthPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berth"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berth"),
            testMode: false);
    }

    /// <summary>The layout file of an arrangement key, which must be 16 lowercase hexadecimal characters.</summary>
    /// <exception cref="ArgumentException">The key is anything else, so it can never leave the layouts folder.</exception>
    public string LayoutFile(string key)
    {
        if (key is null || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException($"'{key}' is not an arrangement key.", nameof(key));
        }

        return Path.Combine(LayoutsFolder, key + ".json");
    }

    /// <summary>Creates the layouts and log folders, and so both roots. Every writer calls it before writing.</summary>
    /// <exception cref="IOException">A folder cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">A folder may not be created.</exception>
    public void EnsureFolders()
    {
        Directory.CreateDirectory(LayoutsFolder);
        Directory.CreateDirectory(LogFolder);
    }

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex KeyPattern();
}
