using Chess.Lib;
using Chess.UCI;
using Shouldly;
using Xunit;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.Tests;

public class UciMoveTests
{
    [Theory]
    [InlineData("e2e4", File.E, Rank.R2, File.E, Rank.R4, PieceType.None)]
    [InlineData("e7e5", File.E, Rank.R7, File.E, Rank.R5, PieceType.None)]
    [InlineData("g1f3", File.G, Rank.R1, File.F, Rank.R3, PieceType.None)]
    [InlineData("a7a8q", File.A, Rank.R7, File.A, Rank.R8, PieceType.Queen)]
    [InlineData("b2b1r", File.B, Rank.R2, File.B, Rank.R1, PieceType.Rook)]
    [InlineData("c7c8n", File.C, Rank.R7, File.C, Rank.R8, PieceType.Knight)]
    [InlineData("d2d1b", File.D, Rank.R2, File.D, Rank.R1, PieceType.Bishop)]
    [InlineData("e1g1", File.E, Rank.R1, File.G, Rank.R1, PieceType.None)]
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

    [Theory]
    [InlineData("e2e4")]
    [InlineData("a7a8q")]
    [InlineData("e1g1")]
    public void Format_RoundTrip(string moveStr)
    {
        var action = UciMove.Parse(moveStr);
        var formatted = UciMove.Format(action);
        formatted.ShouldBe(moveStr);
    }

    [Fact]
    public void Format_SimpleMove()
    {
        var action = Action.DoMove(Position.E2, Position.E4);
        UciMove.Format(action).ShouldBe("e2e4");
    }

    [Fact]
    public void Format_Promotion()
    {
        var action = Action.Promote(Position.A7, Position.A8, PieceType.Queen);
        UciMove.Format(action).ShouldBe("a7a8q");
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2")]
    [InlineData("e2e4e5")]
    public void Parse_InvalidLength_Throws(string moveStr)
    {
        Should.Throw<FormatException>(() => UciMove.Parse(moveStr));
    }

    [Theory]
    [InlineData("i2e4")]
    [InlineData("e9e4")]
    public void Parse_InvalidCoordinate_Throws(string moveStr)
    {
        Should.Throw<FormatException>(() => UciMove.Parse(moveStr));
    }
}
