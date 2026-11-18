using System.Reflection;

namespace Berth.Core;

/// <summary>Facts about the running build that every surface shows the same way.</summary>
internal static class AppInfo
{
    /// <summary>The product version, such as <c>0.1.0</c>, without any <c>+commit</c> build metadata.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        string version = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("The assembly carries no informational version.");
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
