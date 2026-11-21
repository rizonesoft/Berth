using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
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

internal sealed partial class HkcuRunRegistry : IRunRegistry
{
    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorMoreData = 234;
    private const uint RegSz = 1;
    private const uint RegExpandSz = 2;
    private const uint RegBinary = 3;
    private const uint RegDword = 4;
    private const uint RegMultiSz = 7;
    private const uint RegQword = 11;

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

        // Query type and data atomically in one RegQueryValueExW call so concurrent writers cannot mix type and payload,
        // and distinguish ERROR_FILE_NOT_FOUND from real query errors.
        IntPtr hKey = key.Handle.DangerousGetHandle();
        int bufferBytes = 512;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            byte[] buffer = new byte[bufferBytes];
            uint size = (uint)buffer.Length;
            int result = RegQueryValueExW(hKey, valueName, IntPtr.Zero, out uint nativeType, buffer, ref size);
            if (result == ErrorFileNotFound)
            {
                value = null;
                kind = RegistryValueKind.Unknown;
                return false;
            }

            if (result == ErrorMoreData)
            {
                bufferBytes = checked((int)size);
                continue;
            }

            if (result != ErrorSuccess)
            {
                var win32 = new Win32Exception(result);
                throw new IOException(win32.Message, win32);
            }

            kind = MapValueKind(nativeType);
            if (nativeType is RegSz or RegExpandSz)
            {
                int charBytes = (int)size;
                string decoded = charBytes >= 2
                    ? Encoding.Unicode.GetString(buffer, 0, charBytes)
                    : string.Empty;
                // Strip only the single terminating NUL stored by REG_SZ, preserving any embedded NULs for exact comparison.
                value = decoded.EndsWith('\0') ? decoded[..^1] : decoded;
            }
            else
            {
                value = null;
            }

            return true;
        }

        throw new IOException($"Could not read registry value '{valueName}' because its size kept changing.");
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

    private static RegistryValueKind MapValueKind(uint nativeType) => nativeType switch
    {
        RegSz => RegistryValueKind.String,
        RegExpandSz => RegistryValueKind.ExpandString,
        RegBinary => RegistryValueKind.Binary,
        RegDword => RegistryValueKind.DWord,
        RegMultiSz => RegistryValueKind.MultiString,
        RegQword => RegistryValueKind.QWord,
        _ => RegistryValueKind.Unknown,
    };

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegQueryValueExW(
        IntPtr hKey,
        string lpValueName,
        IntPtr lpReserved,
        out uint lpType,
        [Out] byte[] lpData,
        ref uint lpcbData);
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
    public bool IsOn => TryReadIsOn() ?? false;

    /// <summary>
    /// Reads the registry value state, returning <see langword="null"/> when registry access fails so callers can
    /// distinguish an unreadable key from a verified Off state.
    /// </summary>
    internal bool? TryReadIsOn()
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
            return null;
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
                if (!found || kind != RegistryValueKind.String || !string.Equals(readBack, expected, StringComparison.Ordinal))
                {
                    throw new IOException($"The registry value '{_valueName}' did not read back as the exact quoted executable path.");
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
