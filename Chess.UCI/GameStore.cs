using System;
using Chess.Lib;
using File = System.IO.File;

namespace Chess.UCI;

/// <summary>A restored save: the replayed game, who the engine plays (Side.None = no engine), and the
/// mode it was started in.</summary>
public readonly record struct SavedGame(Game Game, Side ComputerSide, GameMode Mode);

/// <summary>
/// Persists a game to a small UCI-format text file and reloads it — the shared "Continue game"
/// store used by every front-end (Android, desktop GUI, ...). Up to three lines:
/// <code>
/// None AcrossTheTable      // line 1: the computer's side ("None" = no engine) + the game mode
/// e2e4 e7e5 g1f3 ...       // line 2: the moves in UCI notation, space-separated
/// 4k3/8/8/8/8/8/8/4K3 b    // line 3: custom games only — the STARTING placement + side to move
/// </code>
/// Replaying the moves rebuilds the full position AND history (castling / en-passant rights,
/// repetition) that a bare FEN snapshot would lose; line 3 only says where that replay starts, and is
/// omitted for a normal game (standard board, White to move).
///
/// <para>The mode has to be stored, not inferred: <see cref="GameMode.AcrossTheTable"/> and
/// <see cref="GameMode.PlayerVsPlayer"/> both have no engine, so a resumed across-the-table game
/// looked like plain hot-seat and stopped turning the frame to face the player to move.</para>
///
/// <para>Backward compatible: a legacy one-token line 1 still loads (the mode is inferred from the
/// computer side, exactly as the hosts used to do). An OLDER build reading a newer save fails its
/// <c>Side</c> parse and falls back to Side.None — a hot-seat game, never a wrong position, and a
/// custom game's moves then refuse to replay so it starts fresh instead.</para>
/// </summary>
public static class GameStore
{
    /// <summary>
    /// Loads a saved game from <paramref name="path"/>, replaying its moves onto the starting
    /// position. Returns null when the file is absent, unreadable, or a move fails to apply (a stale
    /// or incompatible save) — callers then start fresh. <paramref name="log"/> receives diagnostics.
    /// </summary>
    public static SavedGame? TryLoad(string path, System.Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 1) return null;

            var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var computerSide = header.Length > 0 && Enum.TryParse<Side>(header[0], out var cs) ? cs : Side.None;
            var mode = header.Length > 1 && Enum.TryParse<GameMode>(header[1], out var m)
                ? m
                : computerSide == Side.None ? GameMode.PlayerVsPlayer : GameMode.PlayerVsComputer;

            var moves = lines.Length > 1 ? lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];

            var game = ParseStartPosition(lines.Length > 2 ? lines[2] : null);
            foreach (var move in moves)
            {
                if (!game.TryMove(UciMove.Parse(move)).IsMoveOrCapture())
                {
                    log?.Invoke($"[save] replay stopped at '{move}' of {moves.Length} plies");
                    return null; // a move didn't apply -> save is stale/incompatible; start fresh
                }
            }

            log?.Invoke($"[save] loaded {moves.Length} plies, computer={computerSide}, mode={mode}");
            return new SavedGame(game, computerSide, mode);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[save] load failed: {ex.GetType().Name}: {ex.Message}");
            return null; // unreadable / garbled save -> start fresh
        }
    }

    /// <summary>
    /// Saves <paramref name="game"/> to <paramref name="path"/> as the computer side + mode, the UCI
    /// move list, and (only when it isn't the standard opening) the position the moves replay from.
    /// Best-effort: a failed write is swallowed (it must never take down the game).
    /// </summary>
    public static void Save(string path, Game game, Side computerSide, GameMode mode, System.Action<string>? log = null)
    {
        try
        {
            // FormatMoves reconstructs each move WITH its promotion piece (RecordedPly.Action drops
            // Promoted): a bare "e7e8" would make the reload reject the illegal non-promoting pawn
            // move and discard the whole save.
            var moves = string.Join(' ', UciMove.FormatMoves(game));
            var text = $"{computerSide} {mode}\n{moves}";

            // BoardAtPly(-1) is the position before the first ply — for a custom game that's the board
            // the user set up (Game.SetPiece keeps it in step), which the moves are meaningless without.
            var start = game.BoardAtPly(-1);
            var startSide = game.PlyCount % 2 == 0 ? game.CurrentSide : game.CurrentSide.ToOpposite();
            if (start != Board.StandardBoard || startSide != Side.White)
                text += $"\n{start.ToFEN()} {(startSide == Side.Black ? "b" : "w")}";

            File.WriteAllText(path, text);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[save] write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The game the moves replay onto: line 3's placement + side to move, or a standard
    /// opening when the save has no line 3.</summary>
    private static Game ParseStartPosition(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return new Game();

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var board = Board.FromFenPlacement(tokens[0]);
        var side = tokens.Length > 1 && tokens[1] == "b" ? Side.Black : Side.White;
        return new Game(board, side, []);
    }
}
