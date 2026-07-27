using System.Diagnostics;
using Chess.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public sealed class AiEngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Evaluate_StandardBoard_IsZero()
    {
        var board = Board.StandardBoard;
        AiEngine.Evaluate(board, Side.White).ShouldBe(0);
        AiEngine.Evaluate(board, Side.Black).ShouldBe(0);
    }

    [Fact]
    public void Evaluate_MaterialAdvantage_PositiveForSideWithMore()
    {
        // Remove black's queen from D8
        var board = Board.StandardBoard;
        board -= Position.D8;

        AiEngine.Evaluate(board, Side.White).ShouldBeGreaterThan(0);
        AiEngine.Evaluate(board, Side.Black).ShouldBeLessThan(0);
    }

    [Fact]
    public void Evaluate_Symmetric_NegatesForOpposite()
    {
        var board = Board.StandardBoard;
        var whiteScore = AiEngine.Evaluate(board, Side.White);
        var blackScore = AiEngine.Evaluate(board, Side.Black);
        whiteScore.ShouldBe(-blackScore);
    }

    [Fact]
    public void Search_StandardPosition_ReturnsLegalMove()
    {
        var game = new Game();
        var engine = new AiEngine(Side.White, maxDepth: 2);

        var result = engine.Search(game, cancellationToken: Ct);

        result.BestMove.ShouldNotBeNull();
        result.Nodes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Search_RespectsDepthParameter()
    {
        var game = new Game();
        var shallow = new AiEngine(Side.White, maxDepth: 1);
        var deeper = new AiEngine(Side.White, maxDepth: 3);

        var shallowResult = shallow.Search(game, cancellationToken: Ct);
        var deeperResult = deeper.Search(game, cancellationToken: Ct);

        deeperResult.Nodes.ShouldBeGreaterThan(shallowResult.Nodes);
        deeperResult.Depth.ShouldBeGreaterThanOrEqualTo(shallowResult.Depth);
    }

    [Fact]
    public void Search_FinishedGame_ReturnsNull()
    {
        var game = new Game();
        var engine = new AiEngine(Side.Black, maxDepth: 2);

        // White to move, engine is Black — should return null
        var result = engine.Search(game, cancellationToken: Ct);
        result.BestMove.ShouldBeNull();
    }

    [Fact]
    public void PickMove_StandardPosition_ReturnsMove()
    {
        var game = new Game();
        var engine = new AiEngine(Side.White, maxDepth: 2);

        var move = engine.PickMove(game);
        move.ShouldNotBeNull();
    }

    [Fact]
    public void Search_CallsOnDepthComplete_ForEachDepth()
    {
        var game = new Game();
        var engine = new AiEngine(Side.White, maxDepth: 3);
        var depthCallbacks = new List<int>();

        engine.Search(game, onDepthComplete: info => depthCallbacks.Add(info.Depth), cancellationToken: Ct);

        depthCallbacks.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Search_FindsMateInOne()
    {
        // Fool's mate setup: White has played f3, g4; Black to deliver Qh4#
        var game = new Game();
        game.TryMove(Position.F2, Position.F3); // White f3
        game.TryMove(Position.E7, Position.E5); // Black e5
        game.TryMove(Position.G2, Position.G4); // White g4

        var engine = new AiEngine(Side.Black, maxDepth: 2);
        var result = engine.Search(game, cancellationToken: Ct);

        // Black should find Qh4# (Qd8-h4)
        result.BestMove.ShouldNotBeNull();
        result.BestMove.Value.To.ShouldBe(Position.H4);
        result.Score.ShouldBeGreaterThan(AiEngine.MateScore - 100);
    }

    [Fact]
    public void Search_FindsMateByKnightUnderpromotion()
    {
        // Puzzle 5 from "Checkmating Nets – Level 1"
        // White: Kb7, Pe6; Black: Kh7, Qb2, Rh5, Rh8, Be5, Bf7, Nf6, Ng8, + pawns
        // After exf7, white threatens f8=N# (king boxed in by own pieces)
        var board = new Board
        {
            [Position.B7] = (Side.White, PieceType.King),
            [Position.E6] = (Side.White, PieceType.Pawn),
            [Position.H7] = (Side.Black, PieceType.King),
            [Position.B2] = (Side.Black, PieceType.Queen),
            [Position.H5] = (Side.Black, PieceType.Rook),
            [Position.H8] = (Side.Black, PieceType.Rook),
            [Position.E5] = (Side.Black, PieceType.Bishop),
            [Position.F7] = (Side.Black, PieceType.Bishop),
            [Position.F6] = (Side.Black, PieceType.Knight),
            [Position.G8] = (Side.Black, PieceType.Knight),
            [Position.A4] = (Side.Black, PieceType.Pawn),
            [Position.B5] = (Side.Black, PieceType.Pawn),
            [Position.C6] = (Side.Black, PieceType.Pawn),
            [Position.D7] = (Side.Black, PieceType.Pawn),
            [Position.E7] = (Side.Black, PieceType.Pawn),
            [Position.G6] = (Side.Black, PieceType.Pawn),
            [Position.G7] = (Side.Black, PieceType.Pawn),
            [Position.H6] = (Side.Black, PieceType.Pawn),
        };

        var game = new Game(board, Side.White, []);
        var engine = new AiEngine(Side.White, maxDepth: 4);
        var result = engine.Search(game, cancellationToken: Ct);

        // White should find exf7 (e6xf7) setting up unstoppable f8=N#
        result.BestMove.ShouldNotBeNull();
        result.BestMove.Value.From.ShouldBe(Position.E6);
        result.BestMove.Value.To.ShouldBe(Position.F7);
    }

    /// <summary>
    /// A clock that jumps forward a fixed amount every time it is read. The search consults it on a
    /// fixed schedule, so elapsed time becomes a deterministic function of the search itself and a
    /// deadline test stops being a race against the wall clock.
    /// </summary>
    private sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
    {
        private long _reads;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Increment(ref _reads) * step.Ticks;
    }

    [Fact]
    public void Search_BudgetAlreadySpent_StillReturnsCompletedFirstDepth()
    {
        // One read of this clock burns an hour, so the budget is gone the moment the search first
        // looks at it — which is after depth 1, since depth 1 is deliberately exempt from aborting.
        var game = new Game();
        var engine = new AiEngine(Side.White, AiEngine.MaxSearchDepth, new SteppingTimeProvider(TimeSpan.FromHours(1)));
        var reported = new List<int>();

        var result = engine.Search(
            game,
            onDepthComplete: info => reported.Add(info.Depth),
            moveTime: TimeSpan.FromMilliseconds(100),
            cancellationToken: Ct);

        // Depth 1 always completes, so there is always a legal move to play however tight the clock.
        result.BestMove.ShouldNotBeNull();
        result.Depth.ShouldBe(1);
        reported.ShouldBe([1]);
    }

    [Fact]
    public void Search_WithBudget_DoesNotRunToTheDepthCap()
    {
        // Real clock on purpose: this is the only way to exercise an abort that lands *inside* an
        // iteration rather than at a boundary. The bounds are deliberately loose — the assertion is
        // "the budget bounded it", not any particular depth or duration.
        var game = new Game();
        var engine = new AiEngine(Side.White, AiEngine.MaxSearchDepth);

        var started = Stopwatch.GetTimestamp();
        var result = engine.Search(game, moveTime: TimeSpan.FromMilliseconds(50), cancellationToken: Ct);
        var elapsed = Stopwatch.GetElapsedTime(started);

        result.BestMove.ShouldNotBeNull();
        result.Depth.ShouldBeGreaterThanOrEqualTo(1);
        result.Depth.ShouldBeLessThan(AiEngine.MaxSearchDepth);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Search_CancelledMidway_KeepsTheLastCompletedDepth()
    {
        var game = new Game();
        var engine = new AiEngine(Side.White, AiEngine.MaxSearchDepth);
        using var cts = new CancellationTokenSource();
        var reported = new List<int>();

        var result = engine.Search(
            game,
            onDepthComplete: info =>
            {
                reported.Add(info.Depth);
                if (info.Depth == 2)
                {
                    cts.Cancel();
                }
            },
            cancellationToken: cts.Token);

        // Cancelling is not an error — the deepest finished iteration is still a perfectly good answer.
        result.BestMove.ShouldNotBeNull();
        result.Depth.ShouldBe(2);
        reported.ShouldBe([1, 2]);
    }

    [Fact]
    public void Search_PreCancelledToken_StillReturnsLegalMove()
    {
        var game = new Game();
        var engine = new AiEngine(Side.White, AiEngine.MaxSearchDepth);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = engine.Search(game, cancellationToken: cts.Token);

        result.BestMove.ShouldNotBeNull();
        result.Depth.ShouldBe(1);
    }

    [Fact]
    public void Search_ForcedMove_PlaysItWithoutSearching()
    {
        // Ka1 is checked down the a-file by Ra8, and Rc2 covers the second rank, so a2 and b2 are out
        // and Kb1 is the only legal move. There is nothing to compare it against, so spending the
        // budget deciding would just be clock the rest of the game never gets back.
        var board = new Board
        {
            [Position.A1] = (Side.White, PieceType.King),
            [Position.A8] = (Side.Black, PieceType.Rook),
            [Position.C2] = (Side.Black, PieceType.Rook),
            [Position.H6] = (Side.Black, PieceType.King),
        };

        var game = new Game(board, Side.White, []);
        var engine = new AiEngine(Side.White, maxDepth: 4);

        var result = engine.Search(game, cancellationToken: Ct);

        result.BestMove.ShouldNotBeNull();
        result.BestMove.Value.From.ShouldBe(Position.A1);
        result.BestMove.Value.To.ShouldBe(Position.B1);

        // The single evaluated position, and no tree beneath it — this is what says "did not search".
        result.Nodes.ShouldBe(1);
    }

    [Fact]
    public void Search_ChoiceOfMoves_StillSearches()
    {
        // Guards the short circuit against firing when there is a genuine choice: the same position
        // with the second rank uncovered has three king moves, and must be searched properly.
        var board = new Board
        {
            [Position.A1] = (Side.White, PieceType.King),
            [Position.A8] = (Side.Black, PieceType.Rook),
            [Position.H6] = (Side.Black, PieceType.King),
        };

        var game = new Game(board, Side.White, []);
        var engine = new AiEngine(Side.White, maxDepth: 2);

        var result = engine.Search(game, cancellationToken: Ct);

        result.BestMove.ShouldNotBeNull();
        result.Nodes.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Search_ReportsMateDistanceFromRoot_NotFromTheDepthCap()
    {
        // Fool's mate again, but with a cap far deeper than the mate. Iterative deepening finds Qh4#
        // on iteration 2, and the score has to say "mate in one ply" — measured from the root of that
        // iteration, not from MaxDepth, which is where this used to go wrong.
        var game = new Game();
        game.TryMove(Position.F2, Position.F3);
        game.TryMove(Position.E7, Position.E5);
        game.TryMove(Position.G2, Position.G4);

        var engine = new AiEngine(Side.Black, maxDepth: 8);
        var result = engine.Search(game, cancellationToken: Ct);

        result.BestMove.ShouldNotBeNull();
        result.BestMove.Value.To.ShouldBe(Position.H4);
        result.Score.ShouldBe(AiEngine.MateScore - 1);
    }
}
