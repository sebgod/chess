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
    Continue,

    /// <summary>Live LAN game against a discovered peer (see <c>StartupWizard(includeNetworkPlay:)</c>
    /// and <c>Chess.Net</c>). Wired like Player vs Computer — one local human plus one "other" player
    /// — but the "other" is a remote peer over the network; the wizard result's <c>ComputerSide</c>
    /// carries the remote peer's colour.</summary>
    NetworkGame
}
