using Chess.Lib;
using NUnit.Framework;
using Shouldly;

namespace Chess.Tests;

[TestFixture]
public class FenTests
{
    [Test]
    public void FromFenPlacement_StandardBoard_RoundTrips()
    {
        var standard = Board.StandardBoard;
        var fen = standard.ToFEN();
        var parsed = Board.FromFenPlacement(fen);
        parsed.ToFEN().ShouldBe(fen);
    }

    [Test]
    public void FromFenPlacement_StandardBoard_MatchesExpectedFen()
    {
        var fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        var board = Board.FromFenPlacement(fen);
        board.ToFEN().ShouldBe(fen);
    }

    [Test]
    public void FromFenPlacement_EmptyBoard()
    {
        var fen = "8/8/8/8/8/8/8/8";
        var board = Board.FromFenPlacement(fen);
        board.ToFEN().ShouldBe(fen);
    }

    [Test]
    public void FromFenPlacement_AfterE4()
    {
        var fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR";
        var board = Board.FromFenPlacement(fen);
        board.ToFEN().ShouldBe(fen);
    }
}
