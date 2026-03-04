using Chess.UCI;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public class UciFormatterTests
{
    [Fact]
    public void FormatCommand_Uci() =>
        UciFormatter.Format(new UciCommand.UciInit()).ShouldBe("uci");

    [Fact]
    public void FormatCommand_IsReady() =>
        UciFormatter.Format(new UciCommand.IsReady()).ShouldBe("isready");

    [Fact]
    public void FormatCommand_UciNewGame() =>
        UciFormatter.Format(new UciCommand.UciNewGame()).ShouldBe("ucinewgame");

    [Fact]
    public void FormatCommand_Stop() =>
        UciFormatter.Format(new UciCommand.Stop()).ShouldBe("stop");

    [Fact]
    public void FormatCommand_Quit() =>
        UciFormatter.Format(new UciCommand.Quit()).ShouldBe("quit");

    [Fact]
    public void FormatCommand_PositionStartpos() =>
        UciFormatter.Format(new UciCommand.SetPosition(null, [])).ShouldBe("position startpos");

    [Fact]
    public void FormatCommand_PositionStartposWithMoves() =>
        UciFormatter.Format(new UciCommand.SetPosition(null, ["e2e4", "e7e5"]))
            .ShouldBe("position startpos moves e2e4 e7e5");

    [Fact]
    public void FormatCommand_PositionFen() =>
        UciFormatter.Format(new UciCommand.SetPosition("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", []))
            .ShouldBe("position fen rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");

    [Fact]
    public void FormatCommand_GoMovetime() =>
        UciFormatter.Format(new UciCommand.Go(MoveTime: 1000)).ShouldBe("go movetime 1000");

    [Fact]
    public void FormatCommand_GoInfinite() =>
        UciFormatter.Format(new UciCommand.Go(Infinite: true)).ShouldBe("go infinite");

    [Fact]
    public void FormatCommand_GoDepth() =>
        UciFormatter.Format(new UciCommand.Go(Depth: 5)).ShouldBe("go depth 5");

    [Fact]
    public void FormatResponse_UciOk() =>
        UciFormatter.Format(new UciResponse.UciOk()).ShouldBe("uciok");

    [Fact]
    public void FormatResponse_ReadyOk() =>
        UciFormatter.Format(new UciResponse.ReadyOk()).ShouldBe("readyok");

    [Fact]
    public void FormatResponse_Id() =>
        UciFormatter.Format(new UciResponse.Id("name", "Chess.Engine")).ShouldBe("id name Chess.Engine");

    [Fact]
    public void FormatResponse_BestMove() =>
        UciFormatter.Format(new UciResponse.BestMove("e2e4")).ShouldBe("bestmove e2e4");

    [Fact]
    public void FormatResponse_BestMoveWithPonder() =>
        UciFormatter.Format(new UciResponse.BestMove("e2e4", "e7e5"))
            .ShouldBe("bestmove e2e4 ponder e7e5");

    [Fact]
    public void FormatResponse_Info() =>
        UciFormatter.Format(new UciResponse.Info("hello")).ShouldBe("info string hello");

    [Fact]
    public void RoundTrip_CommandFormattedThenParsed()
    {
        var original = new UciCommand.SetPosition(null, ["e2e4", "e7e5"]);
        var formatted = UciFormatter.Format(original);
        var parsed = UciParser.ParseCommand(formatted).ShouldBeOfType<UciCommand.SetPosition>();
        parsed.Fen.ShouldBeNull();
        parsed.Moves.ShouldBe(original.Moves);
    }

    [Fact]
    public void RoundTrip_ResponseFormattedThenParsed()
    {
        var original = new UciResponse.BestMove("e2e4", "e7e5");
        var formatted = UciFormatter.Format(original);
        var parsed = UciParser.ParseResponse(formatted).ShouldBeOfType<UciResponse.BestMove>();
        parsed.Move.ShouldBe(original.Move);
        parsed.Ponder.ShouldBe(original.Ponder);
    }
}
