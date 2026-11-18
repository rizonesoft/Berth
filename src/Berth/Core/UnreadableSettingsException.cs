namespace Berth.Core;

/// <summary>
/// <c>settings.json</c> exists but cannot be read (SPEC §14, §18): it is reported and left as it is, and no save
/// replaces it. This is not a write failure, and is worded differently.
/// </summary>
internal sealed class UnreadableSettingsException : Exception
{
    public UnreadableSettingsException(string reason)
        : base($"The settings file settings.json could not be read: {reason}")
    {
        Reason = reason;
    }

    public UnreadableSettingsException()
        : this(string.Empty)
    {
    }

    public UnreadableSettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = message;
    }

    /// <summary>The file's name, as SPEC §18 shows it.</summary>
    public static string FileName => "settings.json";

    public string Reason { get; }
}
