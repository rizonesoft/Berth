namespace Berth.Core;

/// <summary>
/// The desktop or the monitors cannot be reached right now (Explorer restarting, a locked or disconnected session).
/// The Windows error that caused it travels as the inner exception, so no Windows exception type crosses into Core.
/// </summary>
internal sealed class DesktopUnavailableException : Exception
{
    public DesktopUnavailableException(string message)
        : base(message)
    {
    }

    public DesktopUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
