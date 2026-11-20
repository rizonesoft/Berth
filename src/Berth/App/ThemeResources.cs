using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace Berth.App;

/// <summary>Which generated colour token dictionary Berth merges into <see cref="WpfApplication.Resources"/> (SPEC §2, §3).</summary>
public enum ColorTheme
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// Selects and merges Berth's generated token dictionaries (<c>Tokens.Shared.xaml</c> plus one colour dictionary)
/// into a <see cref="ResourceDictionary"/> so open windows restyle live through <c>DynamicResource</c> (SPEC §2, §3).
/// </summary>
internal static class ThemeResources
{
    public const string DefaultPersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    internal static readonly Uri SharedUri = new("/Berth;component/Theme/Tokens.Shared.xaml", UriKind.Relative);
    internal static readonly Uri LightUri = new("/Berth;component/Theme/Tokens.Light.xaml", UriKind.Relative);
    internal static readonly Uri DarkUri = new("/Berth;component/Theme/Tokens.Dark.xaml", UriKind.Relative);
    internal static readonly Uri HighContrastUri = new("/Berth;component/Theme/Tokens.HighContrast.xaml", UriKind.Relative);

    /// <summary>
    /// Chooses the colour dictionary for the current Windows state: High Contrast wins, otherwise Light when apps
    /// use the light theme and Dark otherwise (SPEC §2, §3).
    /// </summary>
    public static ColorTheme Select(bool highContrast, bool appsUseLightTheme)
    {
        if (highContrast)
        {
            return ColorTheme.HighContrast;
        }

        return appsUseLightTheme ? ColorTheme.Light : ColorTheme.Dark;
    }

    /// <summary>
    /// Reads whether Windows apps use the light theme, matching WPF's <c>ThemeManager</c> fallback:
    /// checks <c>AppsUseLightTheme</c> first; if absent, checks <c>SystemUsesLightTheme</c>; if both are absent
    /// or unreadable, returns <see langword="false"/> (dark).
    /// </summary>
    public static bool ReadAppsUseLightTheme(string registryPath = DefaultPersonalizeRegistryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        return ReadAppsUseLightTheme(valueName => ReadRegistryValue(registryPath, valueName));
    }

    internal static bool ReadAppsUseLightTheme(Func<string, object?> readValue)
    {
        ArgumentNullException.ThrowIfNull(readValue);
        try
        {
            object? appValue = readValue("AppsUseLightTheme");
            if (appValue is int appDword)
            {
                return appDword != 0;
            }

            object? systemValue = readValue("SystemUsesLightTheme");
            return systemValue is int sysDword && sysDword != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Returns the compiled component URI for <paramref name="theme"/>.</summary>
    public static Uri UriFor(ColorTheme theme) => theme switch
    {
        ColorTheme.Light => LightUri,
        ColorTheme.Dark => DarkUri,
        ColorTheme.HighContrast => HighContrastUri,
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown colour theme."),
    };

    /// <summary>
    /// Ensures <c>Tokens.Shared.xaml</c> is merged once and replaces any existing Berth colour dictionary with the one
    /// for <paramref name="theme"/> in place, preserving unrelated merged dictionaries and their precedence.
    /// </summary>
    public static void Apply(ResourceDictionary target, ColorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(target);

        var merged = target.MergedDictionaries;
        if (!merged.Any(IsSharedDictionary))
        {
            merged.Add(LoadDictionary(SharedUri));
        }

        ResourceDictionary replacement = LoadDictionary(UriFor(theme));
        int existingIndex = -1;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (IsColorDictionary(merged[i]))
            {
                if (existingIndex < 0)
                {
                    existingIndex = i;
                }
                else
                {
                    merged.RemoveAt(i);
                    existingIndex--;
                }
            }
        }

        if (existingIndex >= 0)
        {
            merged[existingIndex] = replacement;
        }
        else
        {
            merged.Add(replacement);
        }
    }

    internal static bool IsSharedDictionary(ResourceDictionary dict) =>
        UriEquals(dict.Source, SharedUri);

    internal static bool IsColorDictionary(ResourceDictionary dict) =>
        UriEquals(dict.Source, LightUri)
        || UriEquals(dict.Source, DarkUri)
        || UriEquals(dict.Source, HighContrastUri);

    private static bool UriEquals(Uri? actual, Uri expected)
    {
        if (actual is null)
        {
            return false;
        }

        string normalizedActual = actual.OriginalString.TrimStart('/');
        string normalizedExpected = expected.OriginalString.TrimStart('/');
        return string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceDictionary LoadDictionary(Uri uri) =>
        new() { Source = uri };

    private static object? ReadRegistryValue(string registryPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        return key?.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }
}
