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
    /// <exception cref="InvalidOperationException">
    /// Windows gives no absolute AppData folder (a redirected profile that is unavailable), which must never turn
    /// into a folder relative to wherever Berth was started.
    /// </exception>
    public static BerthPaths FromEnvironment(
        Func<string, string?>? getenv = null,
        Func<Environment.SpecialFolder, string>? folder = null)
    {
        string? data = (getenv ?? Environment.GetEnvironmentVariable)("BERTH_DATA");
        if (!string.IsNullOrWhiteSpace(data))
        {
            BerthPaths redirected = ForRoot(data);
            return new BerthPaths(redirected.DataRoot, redirected.LocalRoot, testMode: true);
        }

        folder ??= special => Environment.GetFolderPath(special, Environment.SpecialFolderOption.DoNotVerify);
        return new BerthPaths(
            Path.Combine(Resolved(folder, Environment.SpecialFolder.ApplicationData), "Berth"),
            Path.Combine(Resolved(folder, Environment.SpecialFolder.LocalApplicationData), "Berth"),
            testMode: false);
    }

    private static string Resolved(Func<Environment.SpecialFolder, string> folder, Environment.SpecialFolder special)
    {
        string path = folder(special);
        return Path.IsPathFullyQualified(path)
            ? path
            : throw new InvalidOperationException($"Windows gives no folder for {special}, so Berth has nowhere to keep its files.");
    }

    /// <summary>Whether <paramref name="key"/> is 16 lowercase hexadecimal characters.</summary>
    public static bool IsKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>The layout file of an arrangement key, which must be 16 lowercase hexadecimal characters.</summary>
    /// <exception cref="ArgumentException">The key is anything else, so it can never leave the layouts folder.</exception>
    public string LayoutFile(string key) => IsKey(key)
        ? Path.Combine(LayoutsFolder, key + ".json")
        : throw new ArgumentException($"'{key}' is not an arrangement key.", nameof(key));

    /// <summary>
    /// Creates the folder that holds <paramref name="file"/>, and its parents. Each writer calls it for its own
    /// destination only, so a log folder that cannot be made never stops a layout save, nor the reverse.
    /// </summary>
    /// <exception cref="IOException">The folder cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be created.</exception>
    public static void EnsureFolderFor(string file)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(file))
            ?? throw new ArgumentException($"'{file}' has no folder.", nameof(file));
        Directory.CreateDirectory(folder);
    }

    [GeneratedRegex(@"^[0-9a-f]{16}\z")]
    private static partial Regex KeyPattern();
}
