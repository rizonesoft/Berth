using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Berth.Desktop;

/// <summary>
/// Registry abstraction for <see cref="RunValue"/> so unit tests can verify read-back mismatches and access refusals
/// deterministically without racing the Windows registry.
/// </summary>
internal interface IRunRegistry
{
    bool TryReadValue(string keyPath, string valueName, out string? value, out RegistryValueKind kind);

    void WriteStringValue(string keyPath, string valueName, string value);

    void DeleteValue(string keyPath, string valueName);
}

internal sealed class HkcuRunRegistry : IRunRegistry
{
    public static HkcuRunRegistry Instance { get; } = new();

    public bool TryReadValue(string keyPath, string valueName, out string? value, out RegistryValueKind kind)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        if (key is null)
        {
            value = null;
            kind = RegistryValueKind.Unknown;
            return false;
        }

        object? raw = key.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            value = null;
            kind = RegistryValueKind.Unknown;
            return false;
        }

        kind = key.GetValueKind(valueName);
        value = raw as string;
        return true;
    }

    public void WriteStringValue(string keyPath, string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new IOException($"Could not open or create registry key 'HKCU\\{keyPath}'.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Reads and writes Berth's "Start with Windows" <c>HKCU\...\Run</c> value (SPEC §11), taking the key path as a
/// parameter so tests never touch the owner's real Run key.
/// </summary>
internal sealed class RunValue
{
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "Berth";

    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly string _executablePath;
    private readonly IRunRegistry _registry;

    public RunValue(
        string keyPath = DefaultKeyPath,
        string valueName = DefaultValueName,
        string? executablePath = null)
        : this(keyPath, valueName, executablePath, HkcuRunRegistry.Instance)
    {
    }

    internal RunValue(
        string keyPath,
        string valueName,
        string? executablePath,
        IRunRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentNullException.ThrowIfNull(registry);

        string resolvedExe = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable path.");
        if (string.IsNullOrWhiteSpace(resolvedExe))
        {
            throw new ArgumentException("Executable path must not be blank.", nameof(executablePath));
        }

        _keyPath = keyPath;
        _valueName = valueName;
        _executablePath = resolvedExe.Trim();
        _registry = registry;
    }

    /// <summary>
    /// True only when the value exists as <see cref="RegistryValueKind.String"/> (<c>REG_SZ</c>) and, unquoted and
    /// compared ignoring case, equals the running executable's path (SPEC §11).
    /// </summary>
    public bool IsOn
    {
        get
        {
            try
            {
                if (!_registry.TryReadValue(_keyPath, _valueName, out string? raw, out RegistryValueKind kind))
                {
                    return false;
                }

                if (kind != RegistryValueKind.String || string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                string unquoted = Unquote(raw);
                return string.Equals(unquoted, _executablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Sets the value to the quoted executable path (<c>REG_SZ</c>) when <paramref name="on"/> is <see langword="true"/>,
    /// or deletes it when <see langword="false"/>, then reads back and throws <see cref="IOException"/> if the registry
    /// state does not match what was asked.
    /// </summary>
    public void Set(bool on)
    {
        try
        {
            if (on)
            {
                string expected = $"\"{_executablePath}\"";
                _registry.WriteStringValue(_keyPath, _valueName, expected);

                bool found = _registry.TryReadValue(_keyPath, _valueName, out string? readBack, out RegistryValueKind kind);
                if (!found || kind != RegistryValueKind.String || !string.Equals(readBack, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"The registry value '{_valueName}' did not read back as the quoted executable path.");
                }
            }
            else
            {
                _registry.DeleteValue(_keyPath, _valueName);

                if (_registry.TryReadValue(_keyPath, _valueName, out _, out _))
                {
                    throw new IOException($"The registry value '{_valueName}' is still present after deletion.");
                }
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }
}
