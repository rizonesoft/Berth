using System.Diagnostics;

namespace Berth.Tests;

/// <summary>
/// A copy of the test assembly started as a helper process. Commands go in one line at a time on standard input
/// and each gets one reply line. The process never opens a console window, is bounded by a timeout, and its whole
/// tree is stopped when the helper is disposed.
/// </summary>
internal sealed class HelperProcess : IDisposable
{
    public const string Switch = "--berth-helper";

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(20);
    private readonly Process process;

    private HelperProcess(Process process) => this.process = process;

    public int Id => process.Id;

    public bool HasExited => process.HasExited;

    public static HelperProcess Start(params string[] args)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("The test process has no path.");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(Switch);
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        var helper = new HelperProcess(Process.Start(info) ?? throw new InvalidOperationException("The helper did not start."));
        string ready = helper.ReadLine();
        if (ready != "ready")
        {
            helper.Dispose();
            throw new InvalidOperationException($"The helper said '{ready}' instead of 'ready'.");
        }

        return helper;
    }

    /// <summary>Sends one command and returns the helper's one-line reply.</summary>
    public string Send(string command)
    {
        process.StandardInput.WriteLine(command);
        process.StandardInput.Flush();
        return ReadLine();
    }

    /// <summary>Ends the helper at once, as a crash would, and waits for it to be gone.</summary>
    public void Kill()
    {
        process.Kill(entireProcessTree: true);
        if (!process.WaitForExit(ReplyTimeout))
        {
            throw new TimeoutException($"Helper {process.Id} did not exit after being killed.");
        }
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(ReplyTimeout);
            }
        }
        catch (InvalidOperationException)
        {
            // The process ended between the check and the kill: nothing is left to stop.
        }

        process.Dispose();
    }

    private string ReadLine()
    {
        Task<string?> line = process.StandardOutput.ReadLineAsync();
        if (!line.Wait(ReplyTimeout))
        {
            throw new TimeoutException($"Helper {process.Id} sent no reply within {ReplyTimeout.TotalSeconds} seconds.");
        }

        return line.Result ?? throw new InvalidOperationException(
            $"Helper {process.Id} closed its output. Standard error: {process.StandardError.ReadToEnd()}");
    }
}
