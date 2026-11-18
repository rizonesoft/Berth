namespace Berth.Core;

/// <summary>A layout file exists but cannot be read (SPEC §14): it is reported and left exactly as it is.</summary>
internal sealed class UnreadableLayoutException : Exception
{
    public UnreadableLayoutException(string fileName, string reason)
        : base($"The layout file {fileName} could not be read: {reason}")
    {
        FileName = fileName;
        Reason = reason;
    }

    public UnreadableLayoutException()
        : this(string.Empty, string.Empty)
    {
    }

    public UnreadableLayoutException(string message, Exception innerException)
        : base(message, innerException)
    {
        FileName = string.Empty;
        Reason = message;
    }

    /// <summary>The file's name within the layouts folder, as the SPEC §9 notice shows it.</summary>
    public string FileName { get; }

    public string Reason { get; }
}
