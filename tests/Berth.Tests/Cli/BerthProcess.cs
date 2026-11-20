using System.Diagnostics;

namespace Berth.Tests.Cli;

/// <summary>Result of a completed child process started through <see cref="BerthProcess"/>.</summary>
internal sealed record BerthProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Elapsed);

/// <summary>
/// Starts the built <c>berth.exe</c> (or a test executable) with <c>CreateNoWindow = true</c>, isolated
/// <c>BERTH_DATA</c> in the child's own environment, bounded stream draining, and guaranteed whole-tree termination on
/// timeout or disposal (AGENTS.md).
/// </summary>
internal sealed class BerthProcess : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StreamDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly Task<string> _stdoutTask;
    private readonly Task<string> _stderrTask;
    private bool _disposed;

    private BerthProcess(Process process)
    {
        _process = process;
        _stdoutTask = process.StandardOutput.ReadToEndAsync();
        _stderrTask = process.StandardError.ReadToEndAsync();
    }

    internal static BerthProcess FromStartedForTest(Process started) => new(started);

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;

    /// <summary>Locates the built <c>berth.exe</c> matching the test assembly's configuration.</summary>
    public static string FindBerthExe()
    {
        string besideTest = Path.Combine(AppContext.BaseDirectory, "berth.exe");
        if (File.Exists(besideTest))
        {
            return besideTest;
        }

        // Fall back to src/Berth/bin/<Config>/net11.0-windows/win-x64/berth.exe when Berth.Tests is run outside win-x64.
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        string config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Berth", "bin", config, "net11.0-windows", "win-x64", "berth.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate built berth.exe from '{AppContext.BaseDirectory}'.");
    }

    public static BerthProcessResult Run(
        string dataRoot,
        string[] args,
        string? testIconPrefix = null,
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        TimeSpan? timeout = null) =>
        RunExecutable(
            FindBerthExe(),
            args,
            dataRoot,
            testIconPrefix,
            extraEnvironment,
            inheritedSeed: null,
            allowUnsetBerthData: false,
            timeout ?? DefaultTimeout);

    internal static BerthProcessResult RunExecutable(
        string exePath,
        string[] args,
        string? dataRoot,
        string? testIconPrefix = null,
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        IReadOnlyDictionary<string, string>? inheritedSeed = null,
        bool allowUnsetBerthData = false,
        TimeSpan? timeout = null)
    {
        using BerthProcess child = StartExecutable(
            exePath,
            args,
            dataRoot,
            testIconPrefix,
            extraEnvironment,
            inheritedSeed,
            allowUnsetBerthData);
        return child.WaitForExit(timeout ?? DefaultTimeout);
    }

    internal static BerthProcess StartExecutable(
        string exePath,
        string[] args,
        string? dataRoot,
        string? testIconPrefix = null,
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        IReadOnlyDictionary<string, string>? inheritedSeed = null,
        bool allowUnsetBerthData = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentNullException.ThrowIfNull(args);
        if (!allowUnsetBerthData)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        }

        if (extraEnvironment is not null)
        {
            foreach (string key in extraEnvironment.Keys)
            {
                if (string.Equals(key, "BERTH_DATA", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("BERTH_DATA is reserved for the TestData root parameter.", nameof(extraEnvironment));
                }
            }
        }

        var info = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        if (inheritedSeed is not null)
        {
            foreach ((string key, string value) in inheritedSeed)
            {
                info.Environment[key] = value;
            }
        }

        foreach (string key in info.Environment.Keys.Where(k => k.StartsWith("BERTH_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            info.Environment.Remove(key);
        }

        if (dataRoot is not null)
        {
            info.Environment["BERTH_DATA"] = dataRoot;
        }

        if (!string.IsNullOrWhiteSpace(testIconPrefix))
        {
            info.Environment["BERTH_TEST_ICON_PREFIX"] = testIconPrefix;
        }

        if (extraEnvironment is not null)
        {
            foreach ((string key, string value) in extraEnvironment)
            {
                info.Environment[key] = value;
            }
        }

        string? existingInstanceKey = info.Environment.Keys.FirstOrDefault(
            k => string.Equals(k, "BERTH_INSTANCE", StringComparison.OrdinalIgnoreCase));
        if (existingInstanceKey is null || string.IsNullOrWhiteSpace(info.Environment[existingInstanceKey]))
        {
            if (existingInstanceKey is not null)
            {
                info.Environment.Remove(existingInstanceKey);
            }

            info.Environment["BERTH_INSTANCE"] = "test-" + Guid.NewGuid().ToString("N");
        }

        Process process = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start '{exePath}'.");
        return new BerthProcess(process);
    }

    public BerthProcessResult WaitForExit(TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        if (!_process.WaitForExit(timeout))
        {
            StopTree();
            throw new TimeoutException($"Process {_process.Id} did not exit within {timeout.TotalSeconds:F1}s.");
        }

        elapsed.Stop();
        if (!Task.WhenAll(_stdoutTask, _stderrTask).Wait(StreamDrainTimeout))
        {
            StopTree();
            throw new TimeoutException(
                $"Process {_process.Id} exited, but stdout/stderr did not finish draining within {StreamDrainTimeout.TotalSeconds:F1}s.");
        }

        return new BerthProcessResult(_process.ExitCode, _stdoutTask.Result, _stderrTask.Result, elapsed.Elapsed);
    }

    public string ReadFirstStdoutLine(TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (_stdoutTask.IsCompleted)
            {
                return _stdoutTask.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
            }

            Thread.Sleep(20);
        }

        throw new TimeoutException($"Process {_process.Id} did not produce stdout within {timeout.TotalSeconds:F1}s.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopTree();
        }
        finally
        {
            _process.Dispose();
        }
    }

    private void StopTree() => HelperProcess.Stop(_process, TimeSpan.FromSeconds(10), kill: true);
}
