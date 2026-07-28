using System.Collections.Immutable;
using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>What one <see cref="GameSession.Tick"/> did.</summary>
public enum SessionOutcome
{
    /// <summary>Nobody had anything to do. A pull driver should sleep; a push driver should stop ticking.</summary>
    Idle,

    /// <summary>A player acted. <see cref="SessionTick.Response"/> and <see cref="SessionTick.ClipRects"/> say what to repaint.</summary>
    Moved,

    /// <summary>Custom-game setup ended and the real game has been built from the placed board.
    /// The driver must call <see cref="GameSession.StartAsync"/> next.</summary>
    SetupFinished,

    /// <summary>Unwind to the menu — the human asked, or a LAN peer left.</summary>
    NeedsRestart,

    /// <summary>The human asked for a fresh game. The driver must call <see cref="GameSession.ResetAsync"/>.</summary>
    NeedsReset,
}

/// <summary>
/// The result of one tick. <paramref name="PlyCommitted"/> is deliberately separate from
/// <paramref name="Response"/>: <see cref="UIResponse.IsUpdate"/> also fires for playback navigation,
/// which moves a view index rather than the game, so it cannot be used to decide whether to persist,
/// save or relay a move. Every front-end previously re-derived this with its own
/// before/after ply-count diff.
/// </summary>
public readonly record struct SessionTick(
    SessionOutcome Outcome,
    UIResponse Response,
    ImmutableArray<RectInt> ClipRects,
    bool PlyCommitted)
{
    public static readonly SessionTick Idle =
        new(SessionOutcome.Idle, UIResponse.None, ImmutableArray<RectInt>.Empty, false);

    internal static SessionTick Control(SessionOutcome outcome) =>
        new(outcome, UIResponse.None, ImmutableArray<RectInt>.Empty, false);
}

/// <summary>
/// One turn of a chess game, driven by whoever owns the loop.
///
/// <para>This is everything <see cref="GameLoop"/> used to do <em>except</em> owning a <c>while</c> and
/// painting. That split is what lets all three front-ends share it: Chess.Console and Chess.GUI poll
/// it from a background thread, Chess.Droid ticks it from an SDL frame callback, and Chess.Web — which
/// is single-threaded and whose rendering is awaited JS interop — ticks it from browser events.</para>
///
/// <para><b>The session never renders.</b> It returns what changed and the driver paints. A session
/// that painted could not work in the browser, where the CPU backend's blit is an <c>await</c>.</para>
///
/// <para><b><see cref="Tick"/> advances at most one ply and is synchronous.</b> One ply, because a
/// front-end may want to repaint between an engine's consecutive moves — Chess.Web paints "thinking…"
/// before each search, since that search blocks its only thread. Synchronous, because Chess.Droid
/// calls it on the SDL thread and must not acquire a second thread touching the same
/// <see cref="GameUI"/>. All the async work — opponent start-up, reset and teardown — lives in the
/// explicitly async members instead.</para>
/// </summary>
public sealed class GameSession
{
    private enum Phase { Setup, AwaitingStart, Playing }

    private readonly IGameDisplay _display;
    private readonly GameMode _gameMode;
    private readonly Side _computerSide;
    private readonly Side _sideToMove;
    private readonly Func<IGamePlayer> _playerFactory;
    private readonly Func<Side, TimeProvider, IGamePlayer>? _opponentFactory;

    private Phase _phase;
    private Game _game;
    private IGamePlayer? _setupPlayer;
    private IGamePlayer? _humanPlayer;
    private IGamePlayer? _whitePlayer;
    private IGamePlayer? _blackPlayer;
    private IGamePlayer? _opponent;
    private Difficulty? _difficulty;

    // Baseline for NeedsReset, captured once the real game exists.
    private Board _initialBoard;
    private string? _initialFen;
    private bool _flipForLocalSide;

    private GameSession(
        IGameDisplay display,
        GameMode gameMode,
        Side computerSide,
        Side sideToMove,
        Func<IGamePlayer> playerFactory,
        Func<Side, TimeProvider, IGamePlayer>? opponentFactory,
        Game game)
    {
        _display = display;
        _gameMode = gameMode;
        _computerSide = computerSide;
        _sideToMove = sideToMove;
        _playerFactory = playerFactory;
        _opponentFactory = opponentFactory;
        _game = game;
    }

    /// <summary>The live game. Replaced by the setup transition and by <see cref="ResetAsync"/>.</summary>
    public Game Game => _game;

    /// <summary>
    /// The current UI. Always read through the display and never cached — a resize rebuilds
    /// <see cref="GameUI"/> as a new instance.
    /// </summary>
    public GameUI UI => _display.UI;

    /// <summary>True while a custom game is still having its pieces placed.</summary>
    public bool IsSetupMode => _phase == Phase.Setup;

    /// <summary>
    /// How hard the opponent plays, changeable while the game runs. <c>null</c> when there is nobody to
    /// ask — player-vs-player — or when the opponent is a person rather than an engine, since a remote
    /// peer has no strength to set.
    ///
    /// <para>The session owns this because the session owns the opponent. A front-end that instead kept
    /// its own reference to whatever the factory returned would be writing game logic — knowing which
    /// concrete player it built and reaching into it — and that reference goes stale the moment a new
    /// session is created, so the write lands on a dead opponent in silence. Setting it here cannot:
    /// applied to the live opponent if there is one, remembered for <see cref="StartAsync"/> if the
    /// game has not begun yet.</para>
    /// </summary>
    public Difficulty? Difficulty
    {
        get => (_opponent as IAdjustableDifficulty)?.Difficulty ?? _difficulty;
        set
        {
            _difficulty = value;

            if (value is { } level && _opponent is IAdjustableDifficulty adjustable)
            {
                adjustable.Difficulty = level;
            }
        }
    }

    /// <summary>
    /// True when the next <see cref="Tick"/> will ask the opponent — an engine or a remote peer — to
    /// move. A front-end whose search would block its UI thread should paint a "thinking" frame
    /// <em>before</em> ticking; by the time the move comes back it is too late.
    /// </summary>
    public bool IsEngineTurn =>
        _phase == Phase.Playing
        && _opponent is not null
        && !_game.IsFinished
        && UI.Mode != GameUIMode.Playback
        && ReferenceEquals(ActivePlayer, _opponent);

    private IGamePlayer? ActivePlayer =>
        UI.Mode == GameUIMode.Playback || _game.IsFinished
            ? _humanPlayer
            : _game.CurrentSide == Side.White ? _whitePlayer : _blackPlayer;

    /// <summary>
    /// Builds a session and its game. A custom game opens in setup mode; anything else is ready for
    /// <see cref="StartAsync"/> immediately. Nothing is painted — the driver renders after this.
    /// </summary>
    /// <param name="opponentFactory">Creates the non-local player for modes that have one. May be
    /// <c>null</c> for front-ends that never face an engine or a peer.</param>
    /// <param name="resumeGame">A loaded game to continue, used as-is so its whole ply history drives
    /// the move list and the engine's <c>position … moves …</c>.</param>
    /// <param name="beginInSetup">Overrides whether to open in setup mode. Defaults to "yes for a
    /// custom game", which is right when starting one fresh but wrong when <em>resuming</em> one that
    /// is already past its setup — the mode says custom for the rest of the game's life.</param>
    public static GameSession Create(
        IGameDisplay display,
        GameMode gameMode,
        Side computerSide,
        Side sideToMove,
        Func<IGamePlayer> playerFactory,
        Func<Side, TimeProvider, IGamePlayer>? opponentFactory = null,
        Game? resumeGame = null,
        bool? beginInSetup = null)
    {
        var game = resumeGame
            ?? (gameMode is GameMode.CustomGameEmpty
                ? new Game(new Board(), sideToMove, [])
                : new Game());

        var session = new GameSession(
            display, gameMode, computerSide, sideToMove, playerFactory, opponentFactory, game);

        display.ResetGame(game);

        if (beginInSetup ?? gameMode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard)
        {
            session._setupPlayer = playerFactory();
            display.UI.IsSetupMode = true;
            session._phase = Phase.Setup;
        }
        else
        {
            session._phase = Phase.AwaitingStart;
        }

        return session;
    }

    /// <summary>
    /// Creates the opponent, waits for it to come up, and pairs the players by colour. Call once the
    /// session is out of setup mode — for a custom game that is after <see cref="Tick"/> reports
    /// <see cref="SessionOutcome.SetupFinished"/>, because the engine's opening position is the board
    /// that was just placed. Nothing is painted; the driver renders after this.
    /// </summary>
    public async Task StartAsync(TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        if (_phase != Phase.AwaitingStart)
        {
            throw new InvalidOperationException($"StartAsync is only valid before play begins (phase: {_phase}).");
        }

        _initialBoard = _game.Board;
        var sideToMoveChar = _sideToMove == Side.Black ? "b" : "w";
        _initialFen = _gameMode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard
            ? _initialBoard.ToFEN() + $" {sideToMoveChar} - - 0 1"
            : null;

        _humanPlayer = _playerFactory();

        // The "other" player is opponent-shaped for PvC/custom AND for a LAN game: a remote peer is a
        // drop-in IGamePlayer (Chess.Net.NetworkPlayer) here, with computerSide = the peer's colour,
        // so the pairing below wires the local human to the local colour automatically.
        if (_opponentFactory is not null
            && _gameMode is GameMode.PlayerVsComputer or GameMode.CustomGameEmpty
                or GameMode.CustomGameStandardBoard or GameMode.NetworkGame)
        {
            _opponent = _opponentFactory(_computerSide, timeProvider);

            // A difficulty chosen before the opponent existed still applies to it.
            if (_difficulty is { } level && _opponent is IAdjustableDifficulty adjustable)
            {
                adjustable.Difficulty = level;
            }

            if (_opponent is IEngineBasedPlayer engineBased)
            {
                await engineBased.InitAsync(_initialFen, cancellationToken);
            }

            (_whitePlayer, _blackPlayer) = _computerSide is Side.White
                ? (_opponent, _humanPlayer)
                : (_humanPlayer, _opponent);
        }
        else
        {
            _opponent = null;
            (_whitePlayer, _blackPlayer) = (_humanPlayer, _humanPlayer);
        }

        // Orient the board to the local player's colour (their pieces at the bottom) when there's a
        // single local human facing an engine/remote opponent; hot-seat (PvP) stays White-at-bottom.
        // computerSide is the opponent's colour, so the local human is the opposite — flip exactly
        // when the opponent plays White. Ctrl+F still overrides at runtime.
        _flipForLocalSide = _opponent is not null && _computerSide is Side.White;
        UI.FlipBoard = _flipForLocalSide;

        _phase = Phase.Playing;
    }

    /// <summary>
    /// <see cref="StartAsync"/> for a driver that cannot leave its thread — Chess.Droid starts games
    /// from an SDL callback. Valid whenever the opponent comes up without genuinely awaiting anything:
    /// the in-process engine has nothing to do, and a LAN peer's socket is already open by this point.
    /// Throws rather than blocking if some future opponent really does need to wait.
    /// </summary>
    public void Start(TimeProvider timeProvider)
    {
        var starting = StartAsync(timeProvider);

        if (!starting.IsCompleted)
        {
            throw new InvalidOperationException(
                "This opponent's start-up is genuinely asynchronous — call StartAsync and await it.");
        }

        // Completed already, so this only observes an exception; it cannot block.
        starting.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gives whoever is due a chance to act, advancing at most one ply. Never blocks on I/O and never
    /// paints.
    /// </summary>
    public SessionTick Tick() => _phase switch
    {
        Phase.Setup => TickSetup(),
        Phase.Playing => TickPlaying(),
        _ => SessionTick.Idle,
    };

    private SessionTick TickSetup()
    {
        // The setup player ends setup itself — the desktop's 's' key, or the display's "▶ Start" chip,
        // which fires re-entrantly from inside HandleMouseDown. Either way it is observed here, on the
        // tick after it happened, so a driver never has to notice the transition for itself.
        if (!UI.IsSetupMode)
        {
            _game = new Game(_game.Board, _sideToMove, []);
            _display.ResetGame(_game);
            _phase = Phase.AwaitingStart;
            return SessionTick.Control(SessionOutcome.SetupFinished);
        }

        return Poll(_setupPlayer!);
    }

    private SessionTick TickPlaying()
    {
        var active = ActivePlayer;

        // Poll the opponent even when it is not its turn, so a peer that leaves is noticed at once
        // rather than whenever the turn next comes round. Safe because every opponent checks the side
        // to move before touching the board (NetworkPlayer/UciPlayer both return null off-turn), so
        // the only thing this can surface out of turn is a control response. Skipped during playback,
        // where it really could be the remote's turn and applying its move would yank the board out
        // from under someone reading history.
        if (_opponent is not null
            && !ReferenceEquals(active, _opponent)
            && UI.Mode != GameUIMode.Playback
            && !_game.IsFinished
            && _opponent.TryMakeMove(UI) is { } fromOpponent
            && fromOpponent.Response.HasFlag(UIResponse.NeedsRestart))
        {
            return SessionTick.Control(SessionOutcome.NeedsRestart);
        }

        return active is null ? SessionTick.Idle : Poll(active);
    }

    private SessionTick Poll(IGamePlayer player)
    {
        // Measured around the player rather than inferred from the response: UIResponse.IsUpdate also
        // fires for playback navigation, and piece placement during setup does not add a ply at all.
        var pliesBefore = _game.PlyCount;
        var result = player.TryMakeMove(UI);

        if (result is not { } moveResult)
        {
            return SessionTick.Idle;
        }

        if (moveResult.Response.HasFlag(UIResponse.NeedsRestart))
        {
            return SessionTick.Control(SessionOutcome.NeedsRestart);
        }

        if (moveResult.Response.HasFlag(UIResponse.NeedsReset))
        {
            return SessionTick.Control(SessionOutcome.NeedsReset);
        }

        return new SessionTick(
            SessionOutcome.Moved,
            moveResult.Response,
            moveResult.ClipRects,
            PlyCommitted: _game.PlyCount > pliesBefore);
    }

    /// <summary>
    /// Starts the game over from the baseline this session began at, and tells the opponent. The
    /// driver renders afterwards.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _game = _initialFen is null ? new Game() : new Game(_initialBoard, _sideToMove, []);
        _display.ResetGame(_game);
        UI.FlipBoard = _flipForLocalSide; // ResetGame builds a fresh GameUI

        if (_opponent is IEngineBasedPlayer engineBased)
        {
            await engineBased.NewGameAsync(_initialFen, cancellationToken);
        }
    }

    /// <summary>Tears down the opponent. The display belongs to the driver, which disposes it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_opponent is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _opponent = null;
    }
}
