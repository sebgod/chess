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
            GameStore.Save(path, game, Side.Black, GameMode.PlayerVsComputer);

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
            GameStore.Save(path, GameFromUci("d2d4"), Side.None, GameMode.PlayerVsPlayer);
            GameStore.TryLoad(path)!.Value.ComputerSide.ShouldBe(Side.None);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(GameMode.PlayerVsPlayer)]
    [InlineData(GameMode.AcrossTheTable)]   // the mode this store had to start carrying: same Side.None
    [InlineData(GameMode.PlayerVsComputer)]
    public void Mode_RoundTrips(GameMode mode)
    {
        var path = TempPath();
        try
        {
            GameStore.Save(path, GameFromUci("d2d4"), Side.None, mode);

            GameStore.TryLoad(path)!.Value.Mode.ShouldBe(mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacySave_WithoutMode_InfersItFromTheComputerSide()
    {
        // Files written before the store carried a mode: one bare Side token on line 1.
        var path = TempPath();
        try
        {
            File.WriteAllLines(path, ["Black", "e2e4 e7e5"]);
            var loaded = GameStore.TryLoad(path);

            loaded.ShouldNotBeNull();
            loaded.Value.ComputerSide.ShouldBe(Side.Black);
            loaded.Value.Mode.ShouldBe(GameMode.PlayerVsComputer);
            loaded.Value.Game.Plies.Count.ShouldBe(2);

            File.WriteAllLines(path, ["None", "d2d4"]);
            GameStore.TryLoad(path)!.Value.Mode.ShouldBe(GameMode.PlayerVsPlayer);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CustomGame_ReplaysFromTheSetUpPosition()
    {
        // A custom game's moves are meaningless without the board they were played on, so the save
        // carries that starting placement — and Game.SetPiece keeps it as "the position before ply 0"
        // even though setup mutates the board in place.
        var path = TempPath();
        try
        {
            var game = new Game(new Board(), Side.Black, []);
            game.SetPiece(Position.E1, new Piece(PieceType.King, Side.White));
            game.SetPiece(Position.E8, new Piece(PieceType.King, Side.Black));
            game.SetPiece(Position.A7, new Piece(PieceType.Rook, Side.Black));
            game.TryMove(UciMove.Parse("a7a2")).IsMoveOrCapture().ShouldBeTrue();

            GameStore.Save(path, game, Side.White, GameMode.CustomGameEmpty);

            var loaded = GameStore.TryLoad(path);
            loaded.ShouldNotBeNull();
            loaded.Value.Mode.ShouldBe(GameMode.CustomGameEmpty);
            loaded.Value.Game.Board.ShouldBe(game.Board);   // the rook landed on a2, kings in place
            loaded.Value.Game.Plies.Count.ShouldBe(1);
            loaded.Value.Game.CurrentSide.ShouldBe(Side.White); // Black moved first, as set up
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NormalGame_OmitsTheStartPositionLine()
    {
        // The standard opening is the default, so a normal save stays two lines — and an older build
        // reading it still sees exactly what it expects.
        var path = TempPath();
        try
        {
            GameStore.Save(path, GameFromUci("e2e4"), Side.None, GameMode.PlayerVsPlayer);

            File.ReadAllLines(path).Length.ShouldBe(2);
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

            GameStore.Save(path, game, Side.None, GameMode.PlayerVsPlayer);

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

    // ── Finished games ──────────────────────────────

    [Fact]
    public void SaveThenLoad_AGameEndedByCheckmate_StillReplays()
    {
        // Regression. The start position used to be derived from CurrentSide and ply parity, but
        // Game repurposes CurrentSide once a game ends -- after checkmate it holds the WINNER (see
        // Game.Winner). Fool's mate ends on ply 4, an EVEN count, so the old derivation read Black
        // and wrote "standard board, Black to move" as the replay origin; the reload then rejected
        // White's f2f3 as illegal and threw the whole save away.
        var path = TempPath();
        try
        {
            var game = GameFromUci("f2f3", "e7e5", "g2g4", "d8h4");
            game.IsFinished.ShouldBeTrue("fool's mate should end the game");
            game.CurrentSide.ShouldBe(Side.Black, "CurrentSide holds the winner after checkmate");

            GameStore.Save(path, game, Side.Black, GameMode.PlayerVsComputer);
            var loaded = GameStore.TryLoad(path);

            loaded.ShouldNotBeNull();
            loaded.Value.Game.PlyCount.ShouldBe(4);
            loaded.Value.Game.IsFinished.ShouldBeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveThenLoad_AGameEndedByStalemate_StillReplays()
    {
        // The other ending, which breaks the old derivation differently: stalemate sets CurrentSide
        // to Side.None, so the parity branch produced a side that is not a side at all.
        var path = TempPath();
        try
        {
            // King+queen smothering the black king into stalemate from a custom position.
            var board = Board.FromFenPlacement("7k/8/8/8/8/8/5Q2/7K");
            var game = new Game(board, Side.White, []);
            game.TryMove(UciMove.Parse("f2f7")).IsMoveOrCapture().ShouldBeTrue();
            game.GameStatus.ShouldBe(GameStatus.Stalemate);

            GameStore.Save(path, game, Side.Black, GameMode.CustomGameStandardBoard);
            var loaded = GameStore.TryLoad(path);

            loaded.ShouldNotBeNull();
            loaded.Value.Game.PlyCount.ShouldBe(1);
            loaded.Value.Game.GameStatus.ShouldBe(GameStatus.Stalemate);
        }
        finally { File.Delete(path); }
    }
}
