using System.Security;
using Berth.Desktop;
using Microsoft.Win32;

namespace Berth.Tests.Desktop;

public sealed class RunValueTests
{
    private const string SampleExe = @"C:\Program Files\Berth\berth.exe";
    private const string OtherExe = @"C:\Old\berth.exe";

    [Fact]
    public void IsOnCoversMissingQuotedUnquotedCaseInsensitiveOtherExecutableDwordAndExpandString()
    {
        using var testKey = new IsolatedRunKey();
        var runValue = new RunValue(testKey.Path, "Berth", SampleExe);

        // 1. Key does not exist -> false.
        Assert.False(runValue.IsOn);

        // 2. Quoted matching REG_SZ -> true.
        testKey.SetValue("Berth", $"\"{SampleExe}\"", RegistryValueKind.String);
        Assert.True(runValue.IsOn);

        // 3. Unquoted matching REG_SZ -> true.
        testKey.SetValue("Berth", SampleExe, RegistryValueKind.String);
        Assert.True(runValue.IsOn);

        // 4. Case-insensitive matching REG_SZ -> true.
        testKey.SetValue("Berth", $"\"{SampleExe.ToUpperInvariant()}\"", RegistryValueKind.String);
        Assert.True(runValue.IsOn);

        // 5. Value naming another executable -> false.
        testKey.SetValue("Berth", $"\"{OtherExe}\"", RegistryValueKind.String);
        Assert.False(runValue.IsOn);

        // 6. Non-string REG_DWORD -> false.
        testKey.SetValue("Berth", 1, RegistryValueKind.DWord);
        Assert.False(runValue.IsOn);

        // 7. REG_EXPAND_SZ with the exact matching path -> false (must be REG_SZ).
        testKey.SetValue("Berth", $"\"{SampleExe}\"", RegistryValueKind.ExpandString);
        Assert.False(runValue.IsOn);
    }

    [Fact]
    public void SetWritesQuotedRegSzReadsBackAndDeletesCleanly()
    {
        using var testKey = new IsolatedRunKey();
        var runValue = new RunValue(testKey.Path, "Berth", SampleExe);

        // Turning off when already missing succeeds cleanly.
        runValue.Set(false);
        Assert.False(runValue.IsOn);

        // Turning on writes exact quoted REG_SZ and IsOn becomes true.
        runValue.Set(true);
        Assert.True(runValue.IsOn);
        Assert.Equal($"\"{SampleExe}\"", testKey.ReadRawString("Berth", out RegistryValueKind kind));
        Assert.Equal(RegistryValueKind.String, kind);

        // Turning off deletes the value completely and IsOn becomes false.
        runValue.Set(false);
        Assert.False(runValue.IsOn);
        Assert.False(testKey.ValueExists("Berth"));
    }

    [Fact]
    public void DefaultExecutablePathUsesEnvironmentProcessPathUnderIsolatedTestKey()
    {
        using var testKey = new IsolatedRunKey();
        string expectedExe = Environment.ProcessPath!;
        var runValue = new RunValue(keyPath: testKey.Path, valueName: "Berth");

        runValue.Set(true);
        Assert.True(runValue.IsOn);
        Assert.Equal($"\"{expectedExe}\"", testKey.ReadRawString("Berth", out _));

        runValue.Set(false);
        Assert.False(runValue.IsOn);
        Assert.False(testKey.ValueExists("Berth"));
    }

    [Fact]
    public void SetThrowsIoExceptionWhenReadBackDoesNotMatchRequestedState()
    {
        // Case 1: After Set(true), registry reads back an unquoted matching path (IsOn would be true, but exact quoted REG_SZ failed).
        var unquotedRegistry = new FakeRunRegistry
        {
            ReadBackExists = true,
            ReadBackValue = SampleExe,
            ReadBackKind = RegistryValueKind.String,
        };
        var runOn = new RunValue(@"Software\Berth.Tests\Fake", "Berth", SampleExe, unquotedRegistry);
        Assert.Throws<IOException>(() => runOn.Set(true));

        // Case 2: After Set(false), a surviving value naming another executable remains (IsOn would be false, but delete failed).
        var survivingRegistry = new FakeRunRegistry
        {
            ReadBackExists = true,
            ReadBackValue = $"\"{OtherExe}\"",
            ReadBackKind = RegistryValueKind.String,
        };
        var runOff = new RunValue(@"Software\Berth.Tests\Fake", "Berth", SampleExe, survivingRegistry);
        Assert.Throws<IOException>(() => runOff.Set(false));
    }

    [Fact]
    public void SetTranslatesAccessAndSecurityFailuresIntoIoExceptionWithOriginalReason()
    {
        var denied = new UnauthorizedAccessException("Access to the registry key is denied.");
        var deniedRegistry = new FakeRunRegistry { ThrowOnWrite = denied };
        var runDenied = new RunValue(@"Software\Berth.Tests\Fake", "Berth", SampleExe, deniedRegistry);

        IOException ex1 = Assert.Throws<IOException>(() => runDenied.Set(true));
        Assert.Equal(denied.Message, ex1.Message);
        Assert.Same(denied, ex1.InnerException);

        var sec = new SecurityException("Requested registry access is not allowed.");
        var secRegistry = new FakeRunRegistry { ThrowOnDelete = sec };
        var runSec = new RunValue(@"Software\Berth.Tests\Fake", "Berth", SampleExe, secRegistry);

        IOException ex2 = Assert.Throws<IOException>(() => runSec.Set(false));
        Assert.Equal(sec.Message, ex2.Message);
        Assert.Same(sec, ex2.InnerException);
    }

    private sealed class IsolatedRunKey : IDisposable
    {
        public string Path { get; } = $@"Software\Berth.Tests\RunValue-{Guid.NewGuid():N}";

        public void SetValue(string name, object value, RegistryValueKind kind)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
            key.SetValue(name, value, kind);
        }

        public string? ReadRawString(string name, out RegistryValueKind kind)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Path, writable: false);
            Assert.NotNull(key);
            kind = key.GetValueKind(name);
            return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        public bool ValueExists(string name)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Path, writable: false);
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not null;
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
        }
    }

    private sealed class FakeRunRegistry : IRunRegistry
    {
        public bool ReadBackExists { get; set; }
        public string? ReadBackValue { get; set; }
        public RegistryValueKind ReadBackKind { get; set; } = RegistryValueKind.String;
        public Exception? ThrowOnWrite { get; set; }
        public Exception? ThrowOnDelete { get; set; }

        public bool TryReadValue(string keyPath, string valueName, out string? value, out RegistryValueKind kind)
        {
            if (ReadBackExists)
            {
                value = ReadBackValue;
                kind = ReadBackKind;
                return true;
            }

            value = null;
            kind = RegistryValueKind.Unknown;
            return false;
        }

        public void WriteStringValue(string keyPath, string valueName, string value)
        {
            if (ThrowOnWrite is not null)
            {
                throw ThrowOnWrite;
            }
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            if (ThrowOnDelete is not null)
            {
                throw ThrowOnDelete;
            }
        }
    }
}
