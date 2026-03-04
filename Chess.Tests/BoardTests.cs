using Chess.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Position;
using static Chess.Lib.Side;

namespace Chess.Tests;

public class BoardTests
{
    [Fact]
    public void TestWhenCallingSetupThen16PiecesAreOnTheBoard()
    {
        var board = Board.StandardBoard;

        var pieces = AllPositions().Select(p => board[p])
            .Where(p => p != Piece.None)
            .ToList();

        pieces.Count.ShouldBe(32);
        pieces.Count(p => p.Side == White).ShouldBe(16);
        pieces.Count(p => p.Side == Black).ShouldBe(16);
    }

    [Fact]
    public void TestWhenNotCallingSetupThenBoardIsEmpty()
    {
        var board = new Board();

        var pieces = AllPositions().Select(p => board[p]).ToList();

        pieces.Count.ShouldBe(64);
        pieces.Count(p => p != Piece.None).ShouldBe(0);
        pieces.Count(p => p == Piece.None).ShouldBe(64);
    }

    [Fact]
    public void TestWhenModifyingStandardBoardThenOnlyLocalCopyIsModified()
    {
        var board = Board.StandardBoard;
        board[A3] = new Piece(PieceType.King, Side.Black);

        Board.StandardBoard[A3].ShouldBe(Piece.None);
    }
}
