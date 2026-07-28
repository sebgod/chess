using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// The two <see cref="IGamePlayer"/> implementations that let the event-driven front-ends stop
/// bypassing <see cref="GameSession"/>.
/// </summary>
public sealed class SharedPlayerTests
{
    private static GameUI NewUi(Game? game = null) => new(game ?? new Game(), 800, 600);

    // ── QueuedInputPlayer ──────────────────────────────────────────

    [Fact]
    public void QueuedInput_WithNothingQueued_IsIdle()
    {
        new QueuedInputPlayer().TryMakeMove(NewUi()).ShouldBeNull();
    }

    [Fact]
    public void QueuedInput_AppliesAQueuedTapOnTheNextPoll()
    {
        var ui = NewUi();
        var player = new QueuedInputPlayer();
        var (x, y) = CentreOf(ui, Position.E2);

        player.PressPointer(x, y);
        player.HasPendingInput.ShouldBeTrue();

        player.TryMakeMove(ui).ShouldNotBeNull();

        // e2 holds a white pawn and White is to move, so the tap selects it.
        ui.Selected.ShouldBe(Position.E2);
    }

    [Fact]
    public void QueuedInput_ConsumesTheEvent_SoItIsNotReapplied()
    {
        var ui = NewUi();
        var player = new QueuedInputPlayer();
        var (x, y) = CentreOf(ui, Position.E2);

        player.PressPointer(x, y);
        player.TryMakeMove(ui).ShouldNotBeNull();

        player.HasPendingInput.ShouldBeFalse();
        player.TryMakeMove(ui).ShouldBeNull();
    }

    [Fact]
    public void QueuedInput_TwoTapsBeforeAPoll_KeepsTheLatest()
    {
        // One event deep on purpose: a stale board coordinate is worse than a dropped one.
        var ui = NewUi();
        var player = new QueuedInputPlayer();
        var (e2X, e2Y) = CentreOf(ui, Position.E2);
        var (d2X, d2Y) = CentreOf(ui, Position.D2);

        player.PressPointer(e2X, e2Y);
        player.PressPointer(d2X, d2Y);
        player.TryMakeMove(ui);

        ui.Selected.ShouldBe(Position.D2);
    }

    [Fact]
    public void QueuedInput_AppliesAQueuedKey()
    {
        var ui = NewUi();
        var player = new QueuedInputPlayer();

        // 'e' is the file-e selector in the keyboard mapping.
        player.PressKey(InputKey.E);

        player.TryMakeMove(ui).ShouldNotBeNull();
    }

    // ── LocalEnginePlayer ──────────────────────────────────────────

    [Fact]
    public void LocalEngine_OffTurn_IsIdle()
    {
        // The guard GameSession's off-turn polling depends on: asked out of turn, it must not move.
        var ui = NewUi();
        var player = new LocalEnginePlayer(Side.Black);

        ui.Game.CurrentSide.ShouldBe(Side.White);
        player.TryMakeMove(ui).ShouldBeNull();
        ui.Game.PlyCount.ShouldBe(0);
    }

    [Fact]
    public void LocalEngine_OnTurn_PlaysALegalMove()
    {
        var ui = NewUi();
        var player = new LocalEnginePlayer(Side.White, Difficulty.Easy);

        player.TryMakeMove(ui).ShouldNotBeNull();

        ui.Game.PlyCount.ShouldBe(1);
    }

    [Fact]
    public void LocalEngine_DuringSetup_IsIdle()
    {
        // Placing pieces is not a position to move in — and Game.SetPiece throws once a ply exists,
        // so an engine move here would also lock the board being built.
        var ui = NewUi();
        ui.IsSetupMode = true;
        var player = new LocalEnginePlayer(Side.White, Difficulty.Easy);

        player.TryMakeMove(ui).ShouldBeNull();
        ui.Game.PlyCount.ShouldBe(0);
    }

    [Fact]
    public void LocalEngine_FinishedGame_IsIdle()
    {
        // Fool's mate: White is mated, so Black has nothing left to play.
        var game = new Game();
        game.TryMove(Position.F2, Position.F3);
        game.TryMove(Position.E7, Position.E5);
        game.TryMove(Position.G2, Position.G4);
        game.TryMove(Position.D8, Position.H4);
        game.IsFinished.ShouldBeTrue();

        new LocalEnginePlayer(Side.White, Difficulty.Easy).TryMakeMove(NewUi(game)).ShouldBeNull();
    }

    [Fact]
    public void LocalEngine_DifficultyChangedMidGame_TakesEffectOnTheNextMove()
    {
        // Asserting the property round-trips proves nothing: the player could still be capturing a
        // depth at construction, which is exactly the shape of bug this guards. So play a move and
        // look at the board instead.
        var easy = ReplyToE4(Difficulty.Easy);
        var hard = ReplyToE4(Difficulty.Hard);

        // The two levels must really disagree here, or the assertion below passes vacuously. Which
        // moves they pick is the evaluation function's business, so this asserts only that they differ.
        easy.ShouldNotBe(hard, "1.e4 must separate the levels for the rest of this test to mean anything");

        // Changing a player that already exists has to be indistinguishable from having built it that
        // way — that equivalence is what lets a front-end offer the change mid-game at all.
        ReplyToE4(Difficulty.Easy, changeTo: Difficulty.Hard).ShouldBe(hard);
    }

    /// <summary>Plays Black's answer to 1.e4 and reports the move it chose.</summary>
    private static (Position From, Position To) ReplyToE4(Difficulty start, Difficulty? changeTo = null)
    {
        var game = new Game();
        game.TryMove(Position.E2, Position.E4);

        var player = new LocalEnginePlayer(Side.Black, start);
        if (changeTo is { } level) player.Difficulty = level;

        player.TryMakeMove(NewUi(game)).ShouldNotBeNull();

        var reply = game.Plies[^1];
        return (reply.From, reply.To);
    }

    private static (int X, int Y) CentreOf(GameUI ui, Position position)
    {
        var rect = ui.SquareRect(position);
        return ((int)((rect.UpperLeft.X + rect.LowerRight.X) / 2), (int)((rect.UpperLeft.Y + rect.LowerRight.Y) / 2));
    }
}
