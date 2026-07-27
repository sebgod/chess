using Chess.UCI;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public class UciParserTests
{
    [Fact]
    public void ParseCommand_Uci() =>
        UciParser.ParseCommand("uci").ShouldBeOfType<UciCommand.UciInit>();

    [Fact]
    public void ParseCommand_IsReady() =>
        UciParser.ParseCommand("isready").ShouldBeOfType<UciCommand.IsReady>();

    [Fact]
    public void ParseCommand_UciNewGame() =>
        UciParser.ParseCommand("ucinewgame").ShouldBeOfType<UciCommand.UciNewGame>();

    [Fact]
    public void ParseCommand_Stop() =>
        UciParser.ParseCommand("stop").ShouldBeOfType<UciCommand.Stop>();

    [Fact]
    public void ParseCommand_Quit() =>
        UciParser.ParseCommand("quit").ShouldBeOfType<UciCommand.Quit>();

    [Fact]
    public void ParseCommand_DebugOn()
    {
        var cmd = UciParser.ParseCommand("debug on").ShouldBeOfType<UciCommand.Debug>();
        cmd.On.ShouldBeTrue();
    }

    [Fact]
    public void ParseCommand_DebugOff()
    {
        var cmd = UciParser.ParseCommand("debug off").ShouldBeOfType<UciCommand.Debug>();
        cmd.On.ShouldBeFalse();
    }

    [Fact]
    public void ParseCommand_PositionStartpos()
    {
        var cmd = UciParser.ParseCommand("position startpos").ShouldBeOfType<UciCommand.SetPosition>();
        cmd.Fen.ShouldBeNull();
        cmd.Moves.ShouldBeEmpty();
    }

    [Fact]
    public void ParseCommand_PositionStartposWithMoves()
    {
        var cmd = UciParser.ParseCommand("position startpos moves e2e4 e7e5").ShouldBeOfType<UciCommand.SetPosition>();
        cmd.Fen.ShouldBeNull();
        cmd.Moves.ShouldBe(new[] { "e2e4", "e7e5" });
    }

    [Fact]
    public void ParseCommand_PositionFen()
    {
        var cmd = UciParser.ParseCommand("position fen rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1")
            .ShouldBeOfType<UciCommand.SetPosition>();
        cmd.Fen.ShouldBe("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");
        cmd.Moves.ShouldBeEmpty();
    }

    [Fact]
    public void ParseCommand_GoMovetime()
    {
        var cmd = UciParser.ParseCommand("go movetime 1000").ShouldBeOfType<UciCommand.Go>();
        cmd.MoveTime.ShouldBe(1000);
        cmd.Infinite.ShouldBeFalse();
    }

    [Fact]
    public void ParseCommand_GoInfinite()
    {
        var cmd = UciParser.ParseCommand("go infinite").ShouldBeOfType<UciCommand.Go>();
        cmd.Infinite.ShouldBeTrue();
    }

    [Fact]
    public void ParseCommand_GoDepth()
    {
        var cmd = UciParser.ParseCommand("go depth 5").ShouldBeOfType<UciCommand.Go>();
        cmd.Depth.ShouldBe(5);
    }

    [Fact]
    public void ParseCommand_GoWithTimes()
    {
        var cmd = UciParser.ParseCommand("go wtime 60000 btime 60000").ShouldBeOfType<UciCommand.Go>();
        cmd.WTime.ShouldBe(60000);
        cmd.BTime.ShouldBe(60000);
    }

    [Fact]
    public void ParseCommand_GoWithIncrementsAndMovesToGo()
    {
        // The shape a GUI actually sends for a 5|3 game with a move count in the period.
        var cmd = UciParser.ParseCommand("go wtime 300000 btime 295000 winc 3000 binc 3000 movestogo 40")
            .ShouldBeOfType<UciCommand.Go>();

        cmd.WTime.ShouldBe(300000);
        cmd.BTime.ShouldBe(295000);
        cmd.WInc.ShouldBe(3000);
        cmd.BInc.ShouldBe(3000);
        cmd.MovesToGo.ShouldBe(40);
    }

    [Fact]
    public void ParseCommand_GoWithTimes_LeavesIncrementsUnset()
    {
        // Absent is not zero: sudden death has no increment at all, and the budget treats the two the
        // same only by choice, not because the parser conflated them.
        var cmd = UciParser.ParseCommand("go wtime 60000 btime 60000").ShouldBeOfType<UciCommand.Go>();

        cmd.WInc.ShouldBeNull();
        cmd.BInc.ShouldBeNull();
        cmd.MovesToGo.ShouldBeNull();
    }

    [Fact]
    public void ParseCommand_ExtraWhitespace()
    {
        var cmd = UciParser.ParseCommand("  position   startpos   moves   e2e4  ").ShouldBeOfType<UciCommand.SetPosition>();
        cmd.Moves.ShouldBe(new[] { "e2e4" });
    }

    [Fact]
    public void ParseCommand_Unknown_ReturnsNull() =>
        UciParser.ParseCommand("unknown command").ShouldBeNull();

    [Fact]
    public void ParseCommand_Empty_ReturnsNull() =>
        UciParser.ParseCommand("").ShouldBeNull();

    [Fact]
    public void ParseResponse_UciOk() =>
        UciParser.ParseResponse("uciok").ShouldBeOfType<UciResponse.UciOk>();

    [Fact]
    public void ParseResponse_ReadyOk() =>
        UciParser.ParseResponse("readyok").ShouldBeOfType<UciResponse.ReadyOk>();

    [Fact]
    public void ParseResponse_Id()
    {
        var resp = UciParser.ParseResponse("id name Chess.Engine").ShouldBeOfType<UciResponse.Id>();
        resp.Type.ShouldBe("name");
        resp.Value.ShouldBe("Chess.Engine");
    }

    [Fact]
    public void ParseResponse_BestMove()
    {
        var resp = UciParser.ParseResponse("bestmove e2e4").ShouldBeOfType<UciResponse.BestMove>();
        resp.Move.ShouldBe("e2e4");
        resp.Ponder.ShouldBeNull();
    }

    [Fact]
    public void ParseResponse_BestMoveWithPonder()
    {
        var resp = UciParser.ParseResponse("bestmove e2e4 ponder e7e5").ShouldBeOfType<UciResponse.BestMove>();
        resp.Move.ShouldBe("e2e4");
        resp.Ponder.ShouldBe("e7e5");
    }

    [Fact]
    public void ParseResponse_Info()
    {
        var resp = UciParser.ParseResponse("info string hello world").ShouldBeOfType<UciResponse.Info>();
        resp.Message.ShouldBe("string hello world");
    }
}
