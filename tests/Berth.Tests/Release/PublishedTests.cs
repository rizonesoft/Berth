using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Berth.Tests.Cli;
using Berth.Tests.Smoke;

namespace Berth.Tests.Release;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "Published")]
public sealed class PublishedTests
{
    private const uint LoadLibraryAsDatafile = 0x00000002;
    private static readonly nint RtManifest = 24;
    private static readonly nint ManifestId = 1;

    [Fact]
    public void PublishedExecutableHasValidChecksumManifestIconAndRunsWithoutSharedDotnetRuntime()
    {
        string? envExe = Environment.GetEnvironmentVariable("BERTH_EXE");
        Assert.False(
            string.IsNullOrWhiteSpace(envExe),
            "BERTH_EXE must be set to the published artifacts/berth.exe (run pwsh scripts/check-all.ps1 -Published).");

        string exePath = BerthProcess.FindBerthExe();
        Assert.Equal(Path.GetFullPath(envExe), exePath, StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(exePath), $"Published executable not found at '{exePath}'.");

        // 1. Independently hash berth.exe and compare against its .sha256 sidecar (<lowercase-hex>  berth.exe).
        string shaPath = exePath + ".sha256";
        Assert.True(File.Exists(shaPath), $"Checksum sidecar not found at '{shaPath}'.");
        string actualHash = ComputeSha256Hex(exePath);
        string sidecar = File.ReadAllText(shaPath).TrimEnd('\r', '\n');
        Assert.Equal($"{actualHash}  berth.exe", sidecar);

        // 2. Inspect embedded Win32 manifest (#1, RT_MANIFEST): exact namespaces and values for consoleAllocationPolicy and dpiAwareness.
        string manifestXml = ReadEmbeddedManifest(exePath);
        XDocument doc = XDocument.Parse(manifestXml);
        XNamespace smi2024 = "http://schemas.microsoft.com/SMI/2024/WindowsSettings";
        XNamespace smi2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";

        XElement? consolePolicy = doc.Descendants(smi2024 + "consoleAllocationPolicy").FirstOrDefault();
        Assert.NotNull(consolePolicy);
        Assert.Equal("detached", consolePolicy!.Value.Trim());

        XElement? dpiAwareness = doc.Descendants(smi2016 + "dpiAwareness").FirstOrDefault();
        Assert.NotNull(dpiAwareness);
        Assert.Equal("PerMonitorV2", dpiAwareness!.Value.Trim());

        // 3. Verify embedded Win32 icon resource is present.
        using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
        Assert.NotNull(icon);
        Assert.True(icon!.Width > 0 && icon.Height > 0);

        // 4. Prove self-contained execution with no shared .NET runtime available: point DOTNET_ROOT to an empty directory,
        //    disable multilevel lookup, and strip dotnet directories from PATH.
        using var data = new TestData();
        string emptyDotnetDir = Path.Combine(data.Root, "empty-dotnet");
        Directory.CreateDirectory(emptyDotnetDir);
        Dictionary<string, string> noRuntimeEnv = BuildNoSharedDotnetEnvironment(emptyDotnetDir);

        BerthProcessResult versionResult = BerthProcess.Run(
            data.Root,
            ["--version"],
            extraEnvironment: noRuntimeEnv);
        Assert.Equal(0, versionResult.ExitCode);
        Assert.Equal("Berth 0.1.0", versionResult.StandardOutput.Trim());
        Assert.Equal(string.Empty, versionResult.StandardError.Trim());

        // 5. Launch the published WPF tray app with no shared .NET runtime available and verify its real MainWindow opens
        //    (checking for the 'Save current layout' button so a crash MessageBox titled 'Berth' can never pass).
        string instanceName = "test-pub-" + Guid.NewGuid().ToString("N");
        noRuntimeEnv["BERTH_INSTANCE"] = instanceName;
        using BerthProcess appProcess = BerthProcess.Start(
            data.Root,
            [],
            testIconPrefix: "berth-pub-probe-" + Guid.NewGuid().ToString("N"),
            extraEnvironment: noRuntimeEnv);

        var window = RunningApp.WaitForWindow(appProcess.Id, TimeSpan.FromSeconds(25), () => appProcess.HasExited);
        Assert.NotNull(window);
        var saveCondition = new System.Windows.Automation.AndCondition(
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                System.Windows.Automation.ControlType.Button),
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty,
                "Save current layout"));
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        System.Windows.Automation.AutomationElement? saveButton = null;
        while (DateTime.UtcNow < deadline && saveButton is null)
        {
            saveButton = window.FindFirst(System.Windows.Automation.TreeScope.Descendants, saveCondition);
            if (saveButton is null)
            {
                Thread.Sleep(50);
            }
        }

        Assert.NotNull(saveButton);
    }

    private static Dictionary<string, string> BuildNoSharedDotnetEnvironment(string emptyDotnetDir)
    {
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string filteredPath = string.Join(
            Path.PathSeparator,
            currentPath
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(entry => !entry.Contains("dotnet", StringComparison.OrdinalIgnoreCase)));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = emptyDotnetDir,
            ["DOTNET_ROOT(x86)"] = emptyDotnetDir,
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["PATH"] = filteredPath,
        };
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string ReadEmbeddedManifest(string exePath)
    {
        nint module = LoadLibraryExW(exePath, 0, LoadLibraryAsDatafile);
        if (module == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"LoadLibraryExW failed for '{exePath}'.");
        }

        try
        {
            nint resource = FindResourceW(module, ManifestId, RtManifest);
            if (resource == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"RT_MANIFEST #1 not found in '{exePath}'.");
            }

            uint size = SizeofResource(module, resource);
            Assert.True(size > 0, "Embedded manifest resource is empty.");

            nint loaded = LoadResource(module, resource);
            if (loaded == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"LoadResource failed for '{exePath}'.");
            }

            nint pointer = LockResource(loaded);
            if (pointer == 0)
            {
                throw new InvalidOperationException($"LockResource returned null for '{exePath}'.");
            }

            byte[] bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, (int)size);
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }
        finally
        {
            _ = FreeLibrary(module);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string lpLibFileName, nint hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint hLibModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint FindResourceW(nint hModule, nint lpName, nint lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(nint hModule, nint hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LoadResource(nint hModule, nint hResInfo);

    [DllImport("kernel32.dll")]
    private static extern nint LockResource(nint hResData);
}
