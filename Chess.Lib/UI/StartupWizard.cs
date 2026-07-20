namespace Chess.Lib.UI;

/// <summary>
/// The startup wizard's state machine and content — the single source of truth for the
/// game-mode flow every front-end presents (GameMode → [PlayAs] → [BoardType → SideToMove →
/// HumanSide]). Hosts drive it with whatever menu widget fits their surface (desktop/web:
/// DIR.Lib's PixelMenuWidget; console: Console.Lib's MenuBase) — present <see cref="Current"/>,
/// feed the picked index to <see cref="Confirm"/>, repeat until <see cref="IsComplete"/>, read
/// <see cref="Result"/>. Item ORDER is load-bearing (Confirm switches on the index), which is
/// exactly why this must not be re-typed per front-end: the three hand-written copies this class
/// replaced had already drifted (the web listed the modes in a different order than the desktop).
///
/// <para>Invariant encoded here once: a custom game is always played against the engine — the
/// HumanSide step assigns the computer the other colour (there is no custom PvP flow).</para>
///
/// <para>"Play by Link" (correspondence chess — the game travels in a shared URL) is opt-in per
/// host via the constructor flag: only front-ends that can produce and consume game links show
/// the entry (today Chess.Web); desktop/console menus stay three items until they grow a link
/// driver of their own.</para>
///
/// <para>"Network game" (live LAN play against a discovered peer — see <c>Chess.Net</c>) is likewise
/// opt-in via <paramref name="includeNetworkPlay"/>: hosts that can open sockets (GUI, Console,
/// Android — not the browser) show it. Like PvC/Link it uses the PlayAs step, so the result's
/// <c>ComputerSide</c> is the remote peer's colour; the host then hands off to a lobby.</para>
///
/// <para>"Continue" (resume the host's persisted in-progress game) is likewise opt-in via
/// <paramref name="includeContinue"/> — hosts pass true only when a resumable save exists (today
/// Chess.Droid, whose back button returns to this menu mid-game). It is PREPENDED so "back to
/// menu, tap the top item" resumes; <see cref="Confirm"/> normalizes the index shift so the
/// standard entries keep their base indices.</para>
/// </summary>
public sealed class StartupWizard(bool includeLinkPlay = false, bool includeContinue = false, bool includeNetworkPlay = false)
{
    /// <summary>♚ Chess ♔ — the wizard title shown on every step.</summary>
    public const string Title = "♚ Chess ♔";

    private enum Phase { GameMode, PlayAs, BoardType, SideToMove, HumanSide }

    private readonly bool _includeLinkPlay = includeLinkPlay;
    private readonly bool _includeContinue = includeContinue;
    private readonly bool _includeNetworkPlay = includeNetworkPlay;
    private Phase _phase = Phase.GameMode;
    private GameMode _gameMode;
    private Side _computerSide;
    private Side _sideToMove = Side.White;

    /// <summary>True once the flow has produced a <see cref="Result"/>; <see cref="Confirm"/>
    /// must not be called after this.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>The chosen configuration. <c>ComputerSide</c> is the side NOT locally
    /// controlled: the engine's colour for Player vs Computer / custom games, the remote
    /// correspondent's colour for <see cref="GameMode.PlayByLink"/>, and <c>Side.None</c> for
    /// Player vs Player (no opponent process at all). Only valid once
    /// <see cref="IsComplete"/>.</summary>
    public (GameMode Mode, Side ComputerSide, Side SideToMove) Result => (_gameMode, _computerSide, _sideToMove);

    /// <summary>The current step's menu content.</summary>
    public (string Title, string Prompt, string[] Items) Current => _phase switch
    {
        Phase.GameMode => (Title, "Select game mode:",
            [.. _includeContinue ? new[] { "Continue game" } : [],
             "Player vs Player", "Player vs Computer", "Custom Game",
             .. _includeLinkPlay ? new[] { "Play by Link" } : [],
             .. _includeNetworkPlay ? new[] { "Network game" } : []]),
        Phase.PlayAs => (Title, "Play as:", ["White", "Black"]),
        Phase.BoardType => (Title, "Starting board:", ["Empty Board", "Standard Board"]),
        Phase.SideToMove => (Title, "Side to move first:", ["White", "Black"]),
        Phase.HumanSide => (Title, "Play as:", ["White", "Black"]),
        _ => ("", "", []),
    };

    /// <summary>
    /// Confirms the item at <paramref name="selected"/> (an index into <see cref="Current"/>'s
    /// Items) and advances the flow — or completes it, upon which <see cref="Result"/> is valid.
    /// </summary>
    public void Confirm(int selected)
    {
        switch (_phase)
        {
            case Phase.GameMode:
                // Continue is prepended; consume it here and normalize the index so the standard
                // entries below keep their base positions (no per-flag index drift).
                if (_includeContinue)
                {
                    if (selected == 0)
                    {
                        // The save defines the real mode/computer side — the host reads them there.
                        _gameMode = GameMode.Continue;
                        _computerSide = Side.None;
                        IsComplete = true;
                        break;
                    }
                    selected -= 1;
                }
                // Explicit indices, not a catch-all else: the item list is 3 base entries plus 0–2
                // trailing optional entries (Play by Link, Network game), and a trailing else would
                // silently misroute an extra index into the Custom Game flow.
                if (selected == 0)
                {
                    _gameMode = GameMode.PlayerVsPlayer;
                    _computerSide = Side.None;
                    IsComplete = true;
                }
                else if (selected == 1)
                {
                    _gameMode = GameMode.PlayerVsComputer;
                    _phase = Phase.PlayAs;
                }
                else if (selected == 2)
                {
                    _phase = Phase.BoardType;
                }
                else
                {
                    // Trailing optional entries, appended after Custom (index 2) in a fixed order.
                    // Both use the same PlayAs step as PvC: "play as" your colour, the other side
                    // (here a remote correspondent/peer, not an engine) lands in _computerSide.
                    var index = 3;
                    if (_includeLinkPlay)
                    {
                        if (selected == index)
                        {
                            _gameMode = GameMode.PlayByLink;
                            _phase = Phase.PlayAs;
                        }
                        index++;
                    }
                    if (_includeNetworkPlay)
                    {
                        if (selected == index)
                        {
                            _gameMode = GameMode.NetworkGame;
                            _phase = Phase.PlayAs;
                        }
                        index++;
                    }
                }
                break;

            case Phase.PlayAs:
                _computerSide = selected == 0 ? Side.Black : Side.White;
                IsComplete = true;
                break;

            case Phase.BoardType:
                _gameMode = selected == 1 ? GameMode.CustomGameStandardBoard : GameMode.CustomGameEmpty;
                _phase = Phase.SideToMove;
                break;

            case Phase.SideToMove:
                _sideToMove = selected == 0 ? Side.White : Side.Black;
                _phase = Phase.HumanSide;
                break;

            case Phase.HumanSide:
                _computerSide = selected == 0 ? Side.Black : Side.White;
                IsComplete = true;
                break;
        }
    }
}
