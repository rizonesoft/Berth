using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Berth.Desktop;

/// <summary>
/// Reports whether the Windows taskbar is light or dark from <c>HKCU\...\Personalize\SystemUsesLightTheme</c>
/// (SPEC §12, first bullet) and raises <see cref="Changed"/> when an <c>ImmersiveColorSet</c> notification
/// observes a new state.
/// </summary>
public sealed class TaskbarTheme
{
    public const string DefaultRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "SystemUsesLightTheme";

    private readonly Func<object?> _readValue;

    public TaskbarTheme(string registryPath = DefaultRegistryPath)
        : this(registryPath, readValue: null)
    {
    }

    internal TaskbarTheme(string registryPath, Func<object?>? readValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        _readValue = readValue ?? (() => ReadRegistryValue(registryPath));
        IsLight = ReadIsLight();
    }

    /// <summary>True when the taskbar uses the light theme (<c>SystemUsesLightTheme == 1</c>); missing, 0, or unreadable means dark.</summary>
    public bool IsLight { get; private set; }

    /// <summary>Raised after <see cref="IsLight"/> changes following an <c>ImmersiveColorSet</c> notification.</summary>
    public event Action? Changed;

    /// <summary>Re-reads the taskbar theme when <paramref name="area"/> is <c>ImmersiveColorSet</c> and raises <see cref="Changed"/> only if <see cref="IsLight"/> changed.</summary>
    public void OnSettingChange(string? area)
    {
        if (!string.Equals(area, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool current = ReadIsLight();
        if (current == IsLight)
        {
            return;
        }

        IsLight = current;
        Changed?.Invoke();
    }

    private bool ReadIsLight()
    {
        try
        {
            return _readValue() is int dword && dword == 1;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static object? ReadRegistryValue(string registryPath)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        return key?.GetValue(ValueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }
}
