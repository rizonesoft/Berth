using System.Security;
using Berth.Desktop;
using Microsoft.Win32;

namespace Berth.Tests.Desktop;

public sealed class TaskbarThemeTests
{
    [Fact]
    public void ReadsMissingZeroOneAndNonDwordValuesFromIsolatedRegistryKey()
    {
        using var testKey = new IsolatedRegistryKey();

        // Missing key => dark (false).
        Assert.False(new TaskbarTheme(testKey.Path).IsLight);

        // Key exists without value => dark (false).
        testKey.Create();
        Assert.False(new TaskbarTheme(testKey.Path).IsLight);

        // DWORD 0 => dark (false).
        testKey.SetDword(0);
        Assert.False(new TaskbarTheme(testKey.Path).IsLight);

        // DWORD 1 => light (true).
        testKey.SetDword(1);
        Assert.True(new TaskbarTheme(testKey.Path).IsLight);

        // Non-DWORD value ("1" string) => dark (false), never throws.
        testKey.SetString("1");
        Assert.False(new TaskbarTheme(testKey.Path).IsLight);
    }

    [Fact]
    public void OnSettingChangeIgnoresOtherAreasUpdatesIsLightBeforeRaisingAndFiresOncePerChange()
    {
        using var testKey = new IsolatedRegistryKey();
        testKey.SetDword(0);

        var theme = new TaskbarTheme(testKey.Path);
        Assert.False(theme.IsLight);

        var observedInsideHandler = new List<bool>();
        theme.Changed += () => observedInsideHandler.Add(theme.IsLight);

        // Flip registry value to 1 while theme is still false; non-matching areas must neither update IsLight nor fire Changed.
        testKey.SetDword(1);
        theme.OnSettingChange(null);
        theme.OnSettingChange(string.Empty);
        theme.OnSettingChange("Environment");
        Assert.False(theme.IsLight);
        Assert.Empty(observedInsideHandler);

        // Matching notification twice raises Changed once and observes IsLight == true inside the handler.
        theme.OnSettingChange("ImmersiveColorSet");
        theme.OnSettingChange("ImmersiveColorSet");
        Assert.True(theme.IsLight);
        Assert.Equal([true], observedInsideHandler);

        // Flip back to 0 and notify case-insensitively: observes IsLight == false inside the handler.
        testKey.SetDword(0);
        theme.OnSettingChange("immersivecolorset");
        theme.OnSettingChange("ImmersiveColorSet");
        Assert.False(theme.IsLight);
        Assert.Equal([true, false], observedInsideHandler);
    }

    [Fact]
    public void RegistryReadFailureFallsBackToDarkAndRaisesChangedOnce()
    {
        bool throwOnRead = false;
        var theme = new TaskbarTheme(
            @"Software\Berth.Tests\Unused",
            () => throwOnRead ? throw new SecurityException("Access denied") : 1);

        Assert.True(theme.IsLight);

        var observed = new List<bool>();
        theme.Changed += () => observed.Add(theme.IsLight);

        throwOnRead = true;
        theme.OnSettingChange("ImmersiveColorSet");
        Assert.False(theme.IsLight);
        Assert.Equal([false], observed);

        // A second failed read stays dark and raises no second event.
        theme.OnSettingChange("ImmersiveColorSet");
        Assert.False(theme.IsLight);
        Assert.Equal([false], observed);
    }

    private sealed class IsolatedRegistryKey : IDisposable
    {
        private const string BasePath = @"Software\Berth.Tests";
        public string Path { get; } = $@"{BasePath}\TaskbarTheme-{Guid.NewGuid():N}";

        public void Create()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
        }

        public void SetDword(int value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
            key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
        }

        public void SetString(string value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
            key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.String);
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
        }
    }
}
