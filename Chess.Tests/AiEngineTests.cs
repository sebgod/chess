using Chess.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public sealed class AiEngineTests
{
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

        var result = engine.Search(game);

        result.BestMove.ShouldNotBeNull();
        result.Nodes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Search_RespectsDepthParameter()
    {
        var game = new Game();
        var shallow = new AiEngine(Side.White, maxDepth: 1);
        var deeper = new AiEngine(Side.White, maxDepth: 3);

        var shallowResult = shallow.Search(game);
        var deeperResult = deeper.Search(game);

        deeperResult.Nodes.ShouldBeGreaterThan(shallowResult.Nodes);
        deeperResult.Depth.ShouldBeGreaterThanOrEqualTo(shallowResult.Depth);
    }

    [Fact]
    public void Search_FinishedGame_ReturnsNull()
    {
        var game = new Game();
        var engine = new AiEngine(Side.Black, maxDepth: 2);

        // White to move, engine is Black — should return null
        var result = engine.Search(game);
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

        engine.Search(game, onDepthComplete: info => depthCallbacks.Add(info.Depth));

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
        var result = engine.Search(game);

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
        var result = engine.Search(game);

        // White should find exf7 (e6xf7) setting up unstoppable f8=N#
        result.BestMove.ShouldNotBeNull();
        result.BestMove.Value.From.ShouldBe(Position.E6);
        result.BestMove.Value.To.ShouldBe(Position.F7);
    }
}
