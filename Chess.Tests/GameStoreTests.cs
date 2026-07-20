using Chess.Lib;
using Chess.UCI;
using Shouldly;
using Xunit;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Chess.Tests;

public class GameStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"chess-gamestore-{System.Guid.NewGuid():N}.uci");

    private static Game GameFromUci(params string[] moves)
    {
        var game = new Game();
        foreach (var m in moves)
            game.TryMove(UciMove.Parse(m)).IsMoveOrCapture().ShouldBeTrue($"move {m} should apply");
        return game;
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsNull()
    {
        GameStore.TryLoad(TempPath()).ShouldBeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMovesAndComputerSide()
    {
        var path = TempPath();
        try
        {
            var game = GameFromUci("e2e4", "e7e5", "g1f3");
            GameStore.Save(path, game, Side.Black);

            var loaded = GameStore.TryLoad(path);
            loaded.ShouldNotBeNull();
            loaded.Value.ComputerSide.ShouldBe(Side.Black);
            loaded.Value.Game.Plies.Count.ShouldBe(3);
            loaded.Value.Game.CurrentSide.ShouldBe(game.CurrentSide);
            loaded.Value.Game.Board.ShouldBe(game.Board);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PlayerVsPlayer_RoundTripsAsSideNone()
    {
        var path = TempPath();
        try
        {
            GameStore.Save(path, GameFromUci("d2d4"), Side.None);
            GameStore.TryLoad(path)!.Value.ComputerSide.ShouldBe(Side.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Promotion_SurvivesTheRoundTrip()
    {
        var path = TempPath();
        try
        {
            // A standard-board line ending in c7xb8=Q (pawn captures the knight and promotes).
            var game = GameFromUci(
                "e2e4", "d7d5", "e4d5", "g8f6", "d5d6", "f6g8", "d6c7", "g8f6", "c7b8q");

            GameStore.Save(path, game, Side.None);

            // The move list must carry the promotion suffix; without it the reload would reject the
            // illegal non-promoting pawn move and discard the whole save (the bug this store fixes).
            File.ReadAllText(path).ShouldContain("c7b8q");

            var loaded = GameStore.TryLoad(path);
            loaded.ShouldNotBeNull();
            loaded.Value.Game.Plies.Count.ShouldBe(game.Plies.Count);
            loaded.Value.Game.Board.ShouldBe(game.Board);
            loaded.Value.Game[Position.B8].ShouldBe(new Piece(PieceType.Queen, Side.White));
        }
        finally { File.Delete(path); }
    }
}
