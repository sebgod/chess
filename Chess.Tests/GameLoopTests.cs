using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.Position;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.Tests;

public class GameLoopTests
{
    private sealed class FakeDisplay(Game game) : IGameDisplay
    {
        public GameUI UI { get; private set; } = new(game, 800, 600);
        public int RenderInitialCount { get; private set; }
        public int RenderMoveCount { get; private set; }

        public void RenderInitial(Game game) => RenderInitialCount++;

        public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile = null)
            => RenderMoveCount++;

        public void HandleResize(Game game) { }

        public void ResetGame(Game game)
        {
            UI = new(game, 800, 600);
        }

        public void Dispose() { }
    }

    private sealed class FakePlayer(Queue<Action> moves, CancellationTokenSource cts) : IGamePlayer
    {
        public int MovesMade { get; private set; }

        public PlayerMoveResult? TryMakeMove(GameUI ui)
        {
            if (ui.Game.IsFinished || moves.Count == 0)
            {
                cts.Cancel();
                return null;
            }

            var action = moves.Dequeue();
            var (response, clips) = ui.TryPerformAction(action);
            MovesMade++;
            return new PlayerMoveResult(response, clips);
        }
    }

    private sealed class FakeEnginePlayer(Queue<Action> moves, CancellationTokenSource cts) : IEngineBasedPlayer
    {
        public string? InitialFen { get; private set; }
        public bool WasInitialized { get; private set; }
        public bool WasDisposed { get; private set; }
        public int MovesMade { get; private set; }

        public Task InitAsync(string? initialFen, CancellationToken ct = default)
        {
            InitialFen = initialFen;
            WasInitialized = true;
            return Task.CompletedTask;
        }

        public Task NewGameAsync(string? initialFen, CancellationToken ct = default)
        {
            InitialFen = initialFen;
            return Task.CompletedTask;
        }

        public PlayerMoveResult? TryMakeMove(GameUI ui)
        {
            if (ui.Game.IsFinished || moves.Count == 0)
            {
                cts.Cancel();
                return null;
            }

            var action = moves.Dequeue();
            var (response, clips) = ui.TryPerformAction(action);
            MovesMade++;
            return new PlayerMoveResult(response, clips);
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task PvP_FoolsMate_EndsInCheckmate()
    {
        // Fool's mate: 1. f3 e5 2. g4 Qh4#
        var cts = new CancellationTokenSource();
        var moves = new Queue<Action>([
            DoMove(F2, F3),
            DoMove(E7, E5),
            DoMove(G2, G4),
            DoMove(D8, H4),
        ]);

        FakeDisplay? display = null;
        var player = new FakePlayer(moves, cts);

        var gameLoop = new GameLoop(
            new FakeTimeProvider(),
            game => display = new FakeDisplay(game),
            () => player,
            (_, _) => throw new InvalidOperationException("Should not create engine in PvP")
        );

        await gameLoop.RunAsync(GameMode.PlayerVsPlayer, Side.None, cts.Token);

        display.ShouldNotBeNull();
        display.UI.Game.IsFinished.ShouldBeTrue();
        display.UI.Game.GameStatus.ShouldBe(GameStatus.Checkmate);
        display.UI.Game.Winner.ShouldBe(Side.Black);
        player.MovesMade.ShouldBe(4);
        display.RenderMoveCount.ShouldBe(4);
    }

    [Fact]
    public async Task PvP_Stalemate()
    {
        // Shortest known stalemate: 1. e3 a5 2. Qh5 Ra6 3. Qxa5 h5 4. h4 Rah6 5. Qxc7 f6 6. Qxd7+ Kf7 7. Qxb7 Qd3 8. Qxb8 Qh7 9. Qxc8 Kg6 10. Qe6 — stalemate
        var cts = new CancellationTokenSource();
        var moves = new Queue<Action>([
            DoMove(E2, E3), DoMove(A7, A5),
            DoMove(D1, H5), DoMove(A8, A6),
            DoMove(H5, A5), DoMove(H7, H5),
            DoMove(H2, H4), DoMove(A6, H6),
            DoMove(A5, C7), DoMove(F7, F6),
            DoMove(C7, D7), DoMove(E8, F7),
            DoMove(D7, B7), DoMove(D8, D3),
            DoMove(B7, B8), DoMove(D3, H7),
            DoMove(B8, C8), DoMove(F7, G6),
            DoMove(C8, E6),
        ]);

        FakeDisplay? display = null;
        var player = new FakePlayer(moves, cts);

        var gameLoop = new GameLoop(
            new FakeTimeProvider(),
            game => display = new FakeDisplay(game),
            () => player,
            (_, _) => throw new InvalidOperationException("Should not create engine in PvP")
        );

        await gameLoop.RunAsync(GameMode.PlayerVsPlayer, Side.None, cts.Token);

        display.ShouldNotBeNull();
        display.UI.Game.IsFinished.ShouldBeTrue();
        display.UI.Game.GameStatus.ShouldBe(GameStatus.Stalemate);
        display.UI.Game.Winner.ShouldBe(Side.None);
        player.MovesMade.ShouldBe(19);
    }

    [Fact]
    public async Task PvC_EngineInitializedAndDisposed()
    {
        // White (human) plays e4, Black (engine) plays e5, then cancel
        var cts = new CancellationTokenSource();
        var humanMoves = new Queue<Action>([DoMove(E2, E4)]);
        var engineMoves = new Queue<Action>([DoMove(E7, E5)]);

        FakeEnginePlayer? engine = null;

        var gameLoop = new GameLoop(
            new FakeTimeProvider(),
            game => new FakeDisplay(game),
            () => new FakePlayer(humanMoves, cts),
            (side, _) =>
            {
                engine = new FakeEnginePlayer(engineMoves, cts);
                return engine;
            }
        );

        await gameLoop.RunAsync(GameMode.PlayerVsComputer, Side.Black, cts.Token);

        engine.ShouldNotBeNull();
        engine.WasInitialized.ShouldBeTrue();
        engine.InitialFen.ShouldBeNull();
        engine.WasDisposed.ShouldBeTrue();
        engine.MovesMade.ShouldBe(1);
    }

    [Fact]
    public async Task PvC_EnginePlaysWhite()
    {
        var cts = new CancellationTokenSource();
        var engineMoves = new Queue<Action>([DoMove(E2, E4)]);
        var humanMoves = new Queue<Action>([DoMove(E7, E5)]);

        FakeEnginePlayer? engine = null;

        var gameLoop = new GameLoop(
            new FakeTimeProvider(),
            game => new FakeDisplay(game),
            () => new FakePlayer(humanMoves, cts),
            (side, _) =>
            {
                side.ShouldBe(Side.White);
                engine = new FakeEnginePlayer(engineMoves, cts);
                return engine;
            }
        );

        await gameLoop.RunAsync(GameMode.PlayerVsComputer, Side.White, cts.Token);

        engine.ShouldNotBeNull();
        engine.MovesMade.ShouldBe(1);
    }

    [Fact]
    public async Task CustomGame_SetupTransitionsToGameplay()
    {
        var cts = new CancellationTokenSource();

        // Setup player ends setup mode immediately
        var setupPlayer = new SetupEndingPlayer(cts);
        var gameplayMoves = new Queue<Action>([DoMove(E2, E4)]);
        var engineMoves = new Queue<Action>([DoMove(E7, E5)]);
        var playerCallCount = 0;

        FakeEnginePlayer? engine = null;
        FakeDisplay? display = null;

        var gameLoop = new GameLoop(
            new FakeTimeProvider(),
            game =>
            {
                display = new FakeDisplay(game);
                return display;
            },
            () =>
            {
                playerCallCount++;
                // First call is for setup, second for gameplay
                if (playerCallCount == 1) return setupPlayer;
                return new FakePlayer(gameplayMoves, cts);
            },
            (side, _) =>
            {
                engine = new FakeEnginePlayer(engineMoves, cts);
                return engine;
            }
        );

        await gameLoop.RunAsync(GameMode.CustomGameStandardBoard, Side.Black, cts.Token);

        playerCallCount.ShouldBe(2);
        engine.ShouldNotBeNull();
        engine.WasInitialized.ShouldBeTrue();
        engine.InitialFen.ShouldNotBeNull();
        engine.WasDisposed.ShouldBeTrue();
        display.ShouldNotBeNull();
        display.RenderInitialCount.ShouldBe(2); // once for setup, once for gameplay
    }

    [Fact]
    public async Task ImmediateCancellation_ExitsCleanly()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var gameLoop = new GameLoop(
            new FakeTimeProvider(),
            game => new FakeDisplay(game),
            () => new FakePlayer(new Queue<Action>(), cts),
            (_, _) => throw new InvalidOperationException("Should not create engine")
        );

        // Should not throw
        await gameLoop.RunAsync(GameMode.PlayerVsPlayer, Side.None, cts.Token);
    }

    /// <summary>
    /// A player that immediately ends setup mode on its first call.
    /// </summary>
    private sealed class SetupEndingPlayer(CancellationTokenSource cts) : IGamePlayer
    {
        public PlayerMoveResult? TryMakeMove(GameUI ui)
        {
            if (!ui.IsSetupMode)
            {
                cts.Cancel();
                return null;
            }

            ui.IsSetupMode = false;
            return new PlayerMoveResult(UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
        }
    }
}
