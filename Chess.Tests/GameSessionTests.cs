using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.Position;

using Action = Chess.Lib.Action;

namespace Chess.Tests;

/// <summary>
/// Covers the surface <see cref="GameSession"/> adds over what <see cref="GameLoop"/> already had.
/// The behaviour it inherited is pinned by <see cref="GameLoopTests"/>, which still drives the loop
/// end-to-end and passed unmodified through the extraction.
/// </summary>
public sealed class GameSessionTests
{
    private sealed class FakeDisplay : IGameDisplay
    {
        private GameUI? _ui;
        public GameUI UI => _ui ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");
        public int RenderInitialCount { get; private set; }
        public int RenderMoveCount { get; private set; }

        public void RenderInitial(Game game) => RenderInitialCount++;
        public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects) => RenderMoveCount++;
        public void HandleResize(Game game) { }
        public void ResetGame(Game game) => _ui = new(game, 800, 600);
        public void Dispose() { }
    }

    /// <summary>Plays queued moves; idle once they run out.</summary>
    private sealed class ScriptedPlayer(params Action[] moves) : IGamePlayer
    {
        private readonly Queue<Action> _moves = new(moves);

        public PlayerMoveResult? TryMakeMove(GameUI ui)
        {
            if (_moves.Count == 0) return null;

            var (response, clips) = ui.TryPerformAction(_moves.Dequeue());
            return new PlayerMoveResult(response, clips);
        }
    }

    /// <summary>Never moves; just reports whatever response it was given, once.</summary>
    private sealed class ControlPlayer(UIResponse response) : IGamePlayer
    {
        private bool _fired;

        public int PollCount { get; private set; }

        public PlayerMoveResult? TryMakeMove(GameUI ui)
        {
            PollCount++;
            if (_fired) return null;
            _fired = true;
            return new PlayerMoveResult(response, ImmutableArray<RectInt>.Empty);
        }
    }

    /// <summary>Stands in for a LAN peer: reports NeedsRestart on "left", otherwise idle off-turn.</summary>
    private sealed class FakePeer(Side remoteSide) : IGamePlayer
    {
        public bool Left { get; set; }
        public int PollCount { get; private set; }

        public PlayerMoveResult? TryMakeMove(GameUI ui)
        {
            PollCount++;

            if (Left)
                return new PlayerMoveResult(UIResponse.NeedsRestart, ImmutableArray<RectInt>.Empty);

            // The guard every real opponent has (NetworkPlayer.cs:34, UciPlayer.cs:45), and the reason
            // polling one off-turn is safe: it declines to touch the board unless it is really its
            // move. This fake never has a move queued, so it is idle either way.
            return null;
        }
    }

    private static async Task<GameSession> PlayingAsync(
        FakeDisplay display, IGamePlayer human, IGamePlayer? opponent = null, Side computerSide = Side.None)
    {
        var session = GameSession.Create(
            display,
            opponent is null ? GameMode.PlayerVsPlayer : GameMode.PlayerVsComputer,
            computerSide,
            Side.White,
            () => human,
            opponent is null ? null : (_, _) => opponent);

        await session.StartAsync(TimeProvider.System, TestContext.Current.CancellationToken);
        return session;
    }

    [Fact]
    public async Task Tick_CommittedMove_ReportsPlyCommitted()
    {
        var display = new FakeDisplay();
        var session = await PlayingAsync(display, new ScriptedPlayer(DoMove(E2, E4)));

        var tick = session.Tick();

        tick.Outcome.ShouldBe(SessionOutcome.Moved);
        tick.PlyCommitted.ShouldBeTrue();
        session.Game.PlyCount.ShouldBe(1);
    }

    [Fact]
    public async Task Tick_PlaybackNavigation_DoesNotCountAsACommittedPly()
    {
        // The whole reason PlyCommitted exists: UIResponse.IsUpdate also fires for history scrubbing,
        // so front-ends keying "save / persist / relay the move" off the response would fire on it.
        var display = new FakeDisplay();
        var session = await PlayingAsync(display, new ScriptedPlayer(DoMove(E2, E4), DoMove(E7, E5)));
        session.Tick();
        session.Tick();
        var beforeNavigation = session.Game.PlyCount;

        display.UI.NavigateBack();
        display.UI.Mode.ShouldBe(GameUIMode.Playback);

        var tick = session.Tick();

        tick.PlyCommitted.ShouldBeFalse();
        session.Game.PlyCount.ShouldBe(beforeNavigation);
    }

    [Fact]
    public async Task Tick_NoInput_IsIdle()
    {
        var session = await PlayingAsync(new FakeDisplay(), new ScriptedPlayer());

        var tick = session.Tick();

        tick.Outcome.ShouldBe(SessionOutcome.Idle);
        tick.PlyCommitted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(UIResponse.NeedsRestart, SessionOutcome.NeedsRestart)]
    [InlineData(UIResponse.NeedsReset, SessionOutcome.NeedsReset)]
    public async Task Tick_ControlResponses_SurfaceAsOutcomes(UIResponse response, SessionOutcome expected)
    {
        var session = await PlayingAsync(new FakeDisplay(), new ControlPlayer(response));

        session.Tick().Outcome.ShouldBe(expected);
    }

    [Fact]
    public async Task IsEngineTurn_TrueOnlyWhenTheOpponentIsAboutToBeAsked()
    {
        // The signal Chess.Web needs to paint "thinking…" before a search blocks its only thread.
        var display = new FakeDisplay();
        var human = new ScriptedPlayer(DoMove(E2, E4));
        var engine = new ScriptedPlayer(DoMove(E7, E5));
        var session = await PlayingAsync(display, human, engine, Side.Black);

        session.IsEngineTurn.ShouldBeFalse(); // White (human) to move

        session.Tick();

        session.IsEngineTurn.ShouldBeTrue();  // Black (engine) to move
    }

    [Fact]
    public async Task IsEngineTurn_FalseDuringPlayback()
    {
        var display = new FakeDisplay();
        var session = await PlayingAsync(display, new ScriptedPlayer(DoMove(E2, E4)), new ScriptedPlayer(), Side.Black);
        session.Tick();
        session.IsEngineTurn.ShouldBeTrue();

        display.UI.NavigateBack();

        session.IsEngineTurn.ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_PeerLeavesWhileItIsOurTurn_IsNoticedImmediately()
    {
        // The bug this extraction fixes. GameLoop only ever polled the player whose turn it was, so
        // NetworkPlayer — the only thing that reports PeerLeft — went unasked while the local human
        // was on move: a peer resigning went unnoticed until you played something.
        var display = new FakeDisplay();
        var peer = new FakePeer(Side.Black);
        var session = await PlayingAsync(display, new ScriptedPlayer(), peer, Side.Black);

        session.Game.CurrentSide.ShouldBe(Side.White); // our turn, not the peer's
        peer.Left = true;

        session.Tick().Outcome.ShouldBe(SessionOutcome.NeedsRestart);
    }

    [Fact]
    public async Task Tick_DoesNotPollTheOpponentOffTurnDuringPlayback()
    {
        // Off-turn polling is safe precisely because opponents check the side to move — but during
        // playback it really can be the remote's turn, and applying its move would yank the board out
        // from under someone reading history.
        var display = new FakeDisplay();
        var peer = new FakePeer(Side.Black);
        var session = await PlayingAsync(display, new ScriptedPlayer(DoMove(E2, E4)), peer, Side.Black);
        session.Tick();

        display.UI.NavigateBack();
        var polledBefore = peer.PollCount;

        session.Tick();

        peer.PollCount.ShouldBe(polledBefore);
    }

    [Fact]
    public async Task Create_PlayerVsPlayer_NeverBuildsAnOpponent()
    {
        var display = new FakeDisplay();
        var session = GameSession.Create(
            display,
            GameMode.PlayerVsPlayer,
            Side.None,
            Side.White,
            () => new ScriptedPlayer(),
            (_, _) => throw new InvalidOperationException("Should not create an opponent for PvP"));

        await Should.NotThrowAsync(() => session.StartAsync(TimeProvider.System, TestContext.Current.CancellationToken));
        session.IsEngineTurn.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_CustomGame_OpensInSetupAndTransitionsOnce()
    {
        var display = new FakeDisplay();
        var session = GameSession.Create(
            display,
            GameMode.CustomGameStandardBoard,
            Side.Black,
            Side.White,
            () => new ScriptedPlayer(),
            (_, _) => new ScriptedPlayer());

        session.IsSetupMode.ShouldBeTrue();
        display.UI.IsSetupMode.ShouldBeTrue();

        // Whoever is driving ends setup (the desktop's 's' key, Droid's "▶ Start" chip).
        display.UI.IsSetupMode = false;

        var tick = session.Tick();

        tick.Outcome.ShouldBe(SessionOutcome.SetupFinished);
        session.IsSetupMode.ShouldBeFalse();
        session.Game.PlyCount.ShouldBe(0); // a fresh game built from the placed board

        // Idempotent: the transition is reported once, not on every subsequent tick.
        session.Tick().Outcome.ShouldBe(SessionOutcome.Idle);
    }

    [Fact]
    public void Create_ResumedCustomGame_CanSkipSetup()
    {
        // A custom game stays GameMode.Custom* for the rest of its life, so deriving "open in setup"
        // from the mode alone would drop a resumed one back into piece placement. Chess.Droid resumes
        // from a save and passes the answer explicitly.
        var display = new FakeDisplay();
        var resumed = new Game();
        resumed.TryMove(Position.E2, Position.E4);

        var session = GameSession.Create(
            display,
            GameMode.CustomGameStandardBoard,
            Side.None,
            Side.White,
            () => new ScriptedPlayer(),
            resumeGame: resumed,
            beginInSetup: false);

        session.IsSetupMode.ShouldBeFalse();
        display.UI.IsSetupMode.ShouldBeFalse();
        session.Game.PlyCount.ShouldBe(1); // the resumed history survived
    }

    [Fact]
    public void Start_SynchronousOpponent_DoesNotNeedTheAsyncPath()
    {
        // Chess.Droid starts games from an SDL callback it must not leave.
        var display = new FakeDisplay();
        var session = GameSession.Create(
            display,
            GameMode.PlayerVsComputer,
            Side.Black,
            Side.White,
            () => new ScriptedPlayer(),
            (side, _) => new LocalEnginePlayer(side, Difficulty.Easy));

        Should.NotThrow(() => session.Start(TimeProvider.System));

        session.Tick().Outcome.ShouldBe(SessionOutcome.Idle); // White (human) to move, nothing queued
    }

    [Fact]
    public async Task StartAsync_BeforeSetupEnds_Throws()
    {
        var session = GameSession.Create(
            new FakeDisplay(), GameMode.CustomGameEmpty, Side.Black, Side.White, () => new ScriptedPlayer());

        await Should.ThrowAsync<InvalidOperationException>(() => session.StartAsync(TimeProvider.System));
    }
}
