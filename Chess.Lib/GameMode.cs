namespace Chess.Lib;

/// <summary>
/// Defines the available game modes.
/// </summary>
public enum GameMode : byte
{
    PlayerVsPlayer,
    PlayerVsComputer,
    CustomGameEmpty,
    CustomGameStandardBoard,
    PlayByLink,

    /// <summary>Resume the host's persisted in-progress game (offered by the startup wizard when
    /// one exists — see <c>StartupWizard(includeContinue:)</c>). The saved game defines the real
    /// mode and computer side; the wizard result carries <c>Side.None</c>.</summary>
    Continue
}
