namespace Berth.Core.Model;

/// <summary>Where the main window was, in physical pixels of the virtual desktop (SPEC §14 <c>window</c>).</summary>
internal sealed record WindowPlacement(int X, int Y, int Width, int Height);

/// <summary>
/// <c>settings.json</c> (SPEC §14): the automatic restore switch and the window placement, which stays null until
/// the window has been closed once. "Start with Windows" is not here: the registry owns it (SPEC §11).
/// </summary>
internal sealed record Settings(int SchemaVersion, bool RestoreAutomatically, WindowPlacement? Window)
{
    public const int CurrentSchemaVersion = 1;

    public static Settings Default { get; } = new(CurrentSchemaVersion, RestoreAutomatically: false, Window: null);
}
