namespace Berth.Window;

/// <summary>
/// Minimal lifecycle seam for the main window so <c>TrayApp</c> can create it lazily, bring it to the front from
/// any activation source, and close it cleanly during application exit.
/// </summary>
internal interface IMainWindow
{
    void BringToFront();

    void CloseForShutdown();
}
