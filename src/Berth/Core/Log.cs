using System.Globalization;
using System.IO;
using System.Text;

namespace Berth.Core;

/// <summary>The level of a log line (SPEC §18).</summary>
internal enum LogLevel
{
    Info,
    Warn,
    Fail,
}

/// <summary>
/// Berth's log, <c>logs\berth.log</c> under the local root (SPEC §18): one header line per event,
/// <c>&lt;UTC time&gt; &lt;LEVEL&gt; &lt;message&gt;</c>, capped at 1 MB with one previous file. The tray app and the
/// command line append to it at once; the size check, the roll and the append run under one named mutex, so they never
/// race. A log call never throws: a failed write is tried once more and then dropped.
/// </summary>
internal sealed class Log
{
    /// <summary>The cap (SPEC §18: 1 MB): a record that would take the file past it starts a new one.</summary>
    public const long Cap = 1_048_576;

    private static readonly TimeSpan MutexWait = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly BerthPaths paths;
    private readonly TimeProvider clock;
    private readonly long cap;
    private readonly Action<int>? afterFailedAttempt;

    public Log(BerthPaths paths, TimeProvider clock)
        : this(paths, clock, Cap, afterFailedAttempt: null)
    {
    }

    /// <summary>As the public constructor, with the cap lowered for a test and a hook told of each failed attempt.</summary>
    internal Log(BerthPaths paths, TimeProvider clock, long cap, Action<int>? afterFailedAttempt)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);
        this.paths = paths;
        this.clock = clock;
        this.cap = cap;
        this.afterFailedAttempt = afterFailedAttempt;
    }

    /// <summary>How many attempts the last call made (1, or 2 after one failure).</summary>
    internal int LastAttempts { get; private set; }

    /// <summary>The marker that ends a record cut to fit the cap.</summary>
    internal const string CutMarker = " [cut]\r\n";

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Fail(string message) => Write(LogLevel.Fail, message);

    /// <summary>A <c>FAIL</c> line, then the exception (type, message and stack) on lines that start with two spaces.</summary>
    public void Fail(string message, Exception error) => Append(level: LogLevel.Fail, message, error);

    public void Write(LogLevel level, string message) => Append(level, message, error: null);

    /// <summary>
    /// The one event of an operation: <c>INFO</c> when it was done, <c>WARN</c> when refused, <c>FAIL</c> when it
    /// failed, with the exception behind an unexpected failure on the lines after it.
    /// </summary>
    public void Result(OperationResult result, string text)
    {
        ArgumentNullException.ThrowIfNull(result);
        LogLevel level = result.Kind switch
        {
            OperationKind.Refused => LogLevel.Warn,
            OperationKind.Failed => LogLevel.Fail,
            _ => LogLevel.Info,
        };
        Append(level, text, result.Error);
    }

    /// <summary>
    /// The record as written: the header, with a CR or LF in the message written as <c>\r</c> or <c>\n</c> so one event
    /// is one header line, then any exception's lines, each starting with two spaces; every line ends CRLF.
    /// </summary>
    internal string Record(LogLevel level, string message, Exception? error)
    {
        ArgumentNullException.ThrowIfNull(message);
        string time = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string word = level switch { LogLevel.Info => "INFO", LogLevel.Warn => "WARN", _ => "FAIL" };
        var text = new StringBuilder();
        text.Append(time).Append(' ').Append(word).Append(' ')
            .Append(message.Replace("\r", @"\r", StringComparison.Ordinal).Replace("\n", @"\n", StringComparison.Ordinal))
            .Append("\r\n");
        if (error is not null)
        {
            foreach (string line in error.ToString().ReplaceLineEndings("\n").Split('\n'))
            {
                text.Append("  ").Append(line).Append("\r\n");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// <paramref name="record"/> as at most <paramref name="limit"/> bytes: whole when it fits, else its longest
    /// prefix that fits with <see cref="CutMarker"/> after it, never splitting a character.
    /// </summary>
    internal static byte[] Fit(string record, long limit)
    {
        byte[] whole = Utf8.GetBytes(record);
        if (whole.Length <= limit)
        {
            return whole;
        }

        long room = limit - Utf8.GetByteCount(CutMarker);
        int chars = 0, bytes = 0;
        while (chars < record.Length)
        {
            int width = char.IsHighSurrogate(record[chars]) && chars + 1 < record.Length ? 2 : 1;
            int size = Utf8.GetByteCount(record.AsSpan(chars, width));
            if (bytes + size > room)
            {
                break;
            }

            bytes += size;
            chars += width;
        }

        return Utf8.GetBytes(record[..chars] + CutMarker);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A log call never throws (SPEC §18): a failed write must not end the operation it records.")]
    private void Append(LogLevel level, string message, Exception? error)
    {
        // Everything happens inside the no-throw boundary: building the record (an exception's text can itself
        // fail), encoding it, and each write.
        LastAttempts = 0;
        try
        {
            string record;
            try
            {
                record = Record(level, message ?? string.Empty, error);
            }
            catch (Exception) when (error is not null)
            {
                record = Record(level, (message ?? string.Empty) + " (The error's details could not be written.)", error: null);
            }

            byte[] bytes = Fit(record, cap);
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                LastAttempts = attempt;
                try
                {
                    WriteOnce(bytes);
                    return;
                }
                catch (Exception)
                {
                    afterFailedAttempt?.Invoke(attempt);
                }
            }
        }
        catch (Exception)
        {
            // Nothing is left that a log call may do but return.
        }
    }

    private void WriteOnce(byte[] bytes)
    {
        string file = paths.LogFile;
        BerthPaths.EnsureFolderFor(file);
        using var mutex = new Mutex(initiallyOwned: false, @"Global\Berth.Log." + OperationLock.FolderHash(paths.LogFolder));
        bool held;
        try
        {
            held = mutex.WaitOne(MutexWait);
        }
        catch (AbandonedMutexException)
        {
            held = true;
        }

        if (!held)
        {
            throw new TimeoutException("Another Berth held the log for too long.");
        }

        try
        {
            var info = new FileInfo(file);
            if (info.Exists && info.Length > 0 && info.Length + bytes.Length > cap)
            {
                File.Move(file, file + ".1", overwrite: true);
            }

            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            stream.Write(bytes);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
