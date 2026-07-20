using System;
using System.Linq;
using Chess.Lib;
using Action = Chess.Lib.Action;
using File = System.IO.File;

namespace Chess.UCI;

/// <summary>
/// Persists a game to a small UCI-format text file and reloads it — the shared "Continue game"
/// store used by every front-end (Android, desktop GUI, ...). The format is two lines:
/// <code>
/// Black                 // line 1: the computer's side, or "None" for player-vs-player
/// e2e4 e7e5 g1f3 ...    // line 2: the moves in UCI notation, space-separated
/// </code>
/// Replaying the moves rebuilds the full position AND history (castling / en-passant rights,
/// repetition) that a bare FEN snapshot would lose.
/// </summary>
public static class GameStore
{
    /// <summary>
    /// Loads a saved game from <paramref name="path"/>, replaying its moves onto a fresh board.
    /// Returns null when the file is absent, unreadable, or a move fails to apply (a stale or
    /// incompatible save) — callers then start fresh. <paramref name="log"/> receives diagnostics.
    /// </summary>
    public static (Game Game, Side ComputerSide)? TryLoad(string path, System.Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 1) return null;

            var computerSide = Enum.TryParse<Side>(lines[0].Trim(), out var cs) ? cs : Side.None;
            var moves = lines.Length > 1 ? lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];

            var game = new Game();
            foreach (var move in moves)
            {
                if (!game.TryMove(UciMove.Parse(move)).IsMoveOrCapture())
                {
                    log?.Invoke($"[save] replay stopped at '{move}' of {moves.Length} plies");
                    return null; // a move didn't apply -> save is stale/incompatible; start fresh
                }
            }

            log?.Invoke($"[save] loaded {moves.Length} plies, computer={computerSide}");
            return (game, computerSide);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[save] load failed: {ex.GetType().Name}: {ex.Message}");
            return null; // unreadable / garbled save -> start fresh
        }
    }

    /// <summary>
    /// Saves <paramref name="game"/> to <paramref name="path"/> as the computer side plus the UCI
    /// move list. Best-effort: a failed write is swallowed (it must never take down the game).
    /// </summary>
    public static void Save(string path, Game game, Side computerSide, System.Action<string>? log = null)
    {
        try
        {
            var moves = string.Join(' ', game.Plies.Select(FormatPly));
            File.WriteAllText(path, $"{computerSide}\n{moves}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[save] write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Reconstruct the move WITH its promotion piece: the computed RecordedPly.Action drops Promoted,
    // so formatting that directly would write "e7e8" instead of "e7e8q" and the reload would reject
    // the illegal non-promoting pawn move, discarding the whole save.
    private static string FormatPly(RecordedPly ply) =>
        UciMove.Format(ply.Promoted is not PieceType.None
            ? Action.Promote(ply.From, ply.To, ply.Promoted)
            : Action.DoMove(ply.From, ply.To));
}
