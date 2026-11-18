namespace Berth.Tests.Core;

/// <summary>Files a save may leave beside its target (<c>name.</c> and a suffix), never the target itself.</summary>
internal static class Leftovers
{
    public static string[] Of(string folder, string name) =>
        Directory.GetFiles(folder).Where(f => Path.GetFileName(f) is { } n && n.Length > name.Length + 1 && n.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
}
