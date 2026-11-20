using System.Diagnostics;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;

namespace Berth.Tests.Desktop;

/// <summary>
/// Creates a single unique probe file on the desktop, polls exclusively through <see cref="ScopedDesktopIcons"/>
/// until Explorer surfaces it, and deletes only the exact file it created on disposal or post-creation startup failure.
/// </summary>
internal sealed class ProbeIcon : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private bool _ownsFile;

    private ProbeIcon(string prefix, string filePath, ScopedDesktopIcons scopedDesktop, IconPosition icon)
    {
        Prefix = prefix;
        FilePath = filePath;
        ScopedDesktop = scopedDesktop;
        Icon = icon;
        _ownsFile = true;
    }

    public string Prefix { get; }
    public string FilePath { get; }
    public ScopedDesktopIcons ScopedDesktop { get; }
    public IconPosition Icon { get; }

    public static ProbeIcon Create(IDesktopIcons desktop, TimeSpan? timeout = null) =>
        CreateInFolder(
            desktop,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            prefix: null,
            timeout ?? DefaultTimeout,
            DefaultPollInterval);

    internal static ProbeIcon CreateInFolder(
        IDesktopIcons desktop,
        string desktopDirectory,
        string? prefix = null,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);

        string probePrefix = string.IsNullOrWhiteSpace(prefix)
            ? $"berth-test-probe-{Guid.NewGuid():N}"
            : prefix;
        string filePath = Path.Combine(desktopDirectory, $"{probePrefix}.txt");
        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        TimeSpan effectivePoll = pollInterval ?? DefaultPollInterval;

        var scoped = new ScopedDesktopIcons(desktop, probePrefix);
        bool created = false;
        try
        {
            using (new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
            }

            IconPosition? found = WaitForProbeIcon(scoped, effectiveTimeout, effectivePoll);
            if (found is null)
            {
                throw new TimeoutException(
                    $"Probe icon '{probePrefix}' did not appear on the desktop within {effectiveTimeout.TotalSeconds:F1}s.");
            }

            return new ProbeIcon(probePrefix, filePath, scoped, found);
        }
        catch
        {
            if (created)
            {
                DeleteOwnedFile(filePath);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (!_ownsFile)
        {
            return;
        }

        DeleteOwnedFile(FilePath);
        _ownsFile = false;
    }

    private static IconPosition? WaitForProbeIcon(
        ScopedDesktopIcons scoped,
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            IReadOnlyList<IconPosition> icons = scoped.ReadIcons();
            if (icons.Count == 1)
            {
                return icons[0];
            }

            if (icons.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Expected at most one icon matching probe prefix, found {icons.Count}.");
            }

            if (elapsed.Elapsed >= timeout)
            {
                return null;
            }

            Thread.Sleep(pollInterval);
        }
    }

    private static void DeleteOwnedFile(string filePath)
    {
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (Exception ex) when (
                attempt < maxAttempts
                && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(25);
            }
        }
    }
}
