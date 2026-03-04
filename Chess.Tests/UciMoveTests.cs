using Chess.Lib;
using Chess.UCI;
using NUnit.Framework;
using Shouldly;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.Tests;

[TestFixture]
public class UciMoveTests
{
    [TestCase("e2e4", File.E, Rank.R2, File.E, Rank.R4, PieceType.None)]
    [TestCase("e7e5", File.E, Rank.R7, File.E, Rank.R5, PieceType.None)]
    [TestCase("g1f3", File.G, Rank.R1, File.F, Rank.R3, PieceType.None)]
    [TestCase("a7a8q", File.A, Rank.R7, File.A, Rank.R8, PieceType.Queen)]
    [TestCase("b2b1r", File.B, Rank.R2, File.B, Rank.R1, PieceType.Rook)]
    [TestCase("c7c8n", File.C, Rank.R7, File.C, Rank.R8, PieceType.Knight)]
    [TestCase("d2d1b", File.D, Rank.R2, File.D, Rank.R1, PieceType.Bishop)]
    [TestCase("e1g1", File.E, Rank.R1, File.G, Rank.R1, PieceType.None)]
    public void Parse_ValidMove(string moveStr, File fromFile, Rank fromRank, File toFile, Rank toRank, PieceType promoted)
    {
        var action = UciMove.Parse(moveStr);

        action.From.File.ShouldBe(fromFile);
        action.From.Rank.ShouldBe(fromRank);
        action.To.File.ShouldBe(toFile);
        action.To.Rank.ShouldBe(toRank);
        action.Promoted.ShouldBe(promoted);
        action.IsMove.ShouldBeTrue();
    }

    [TestCase("e2e4")]
    [TestCase("a7a8q")]
    [TestCase("e1g1")]
    public void Format_RoundTrip(string moveStr)
    {
        var action = UciMove.Parse(moveStr);
        var formatted = UciMove.Format(action);
        formatted.ShouldBe(moveStr);
    }

    [Test]
    public void Format_SimpleMove()
    {
        var action = Action.DoMove(Position.E2, Position.E4);
        UciMove.Format(action).ShouldBe("e2e4");
    }

    [Test]
    public void Format_Promotion()
    {
        var action = Action.Promote(Position.A7, Position.A8, PieceType.Queen);
        UciMove.Format(action).ShouldBe("a7a8q");
    }

    [TestCase("")]
    [TestCase("e2")]
    [TestCase("e2e4e5")]
    public void Parse_InvalidLength_Throws(string moveStr)
    {
        Should.Throw<FormatException>(() => UciMove.Parse(moveStr));
    }

    [TestCase("i2e4")]
    [TestCase("e9e4")]
    public void Parse_InvalidCoordinate_Throws(string moveStr)
    {
        Should.Throw<FormatException>(() => UciMove.Parse(moveStr));
    }
}
