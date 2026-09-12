using System;
using Chess.Lib;
using File = System.IO.File;

namespace Chess.UCI;

/// <summary>A restored save: the replayed game, who the engine plays (Side.None = no engine), and the
/// mode it was started in.</summary>
public readonly record struct SavedGame(Game Game, Side ComputerSide, GameMode Mode)
{
    /// <summary>The other player's display name, or "" when the save predates the field or the mode
    /// has nobody to name. Init-only rather than a fourth positional parameter so every existing
    /// three-argument construction keeps compiling.</summary>
    public string Opponent { get; init; } = "";

    /// <summary>When this save last changed. Null on a save written before the field existed — which
    /// a caller should read as "unknown", not as the epoch, since the difference decides whether a
    /// game looks stale.</summary>
    public DateTimeOffset? LastMove { get; init; }
}

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
/// <para>Line 1 may carry further <c>key=value</c> tokens after the two positional ones — today
/// <c>opp=</c> (the opponent's display name, URL-encoded so it cannot contain a token-splitting
/// space) and <c>at=</c> (unix seconds when the entry last changed). They go on line 1 rather than a
/// line 4 because line 3 is CONDITIONAL, so a fourth line has no fixed position to be found at. An
/// older build reads only header[0] and header[1] and ignores the rest, so the format stays
/// compatible in both directions with no version bump.</para>
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
            return new SavedGame(game, computerSide, mode)
            {
                Opponent = ReadToken(header, OpponentKey) is { } name ? Decode(name) : "",
                LastMove = ReadToken(header, LastMoveKey) is { } at && long.TryParse(at, out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix)
                    : null,
            };
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
    public static void Save(
        string path, Game game, Side computerSide, GameMode mode, System.Action<string>? log = null,
        string opponent = "", DateTimeOffset? lastMove = null)
    {
        try
        {
            // FormatMoves reconstructs each move WITH its promotion piece (RecordedPly.Action drops
            // Promoted): a bare "e7e8" would make the reload reject the illegal non-promoting pawn
            // move and discard the whole save.
            var moves = string.Join(' ', UciMove.FormatMoves(game));
            var line1 = $"{computerSide} {mode}";
            if (!string.IsNullOrEmpty(opponent)) line1 += $" {OpponentKey}={Encode(opponent)}";
            if (lastMove is { } at) line1 += $" {LastMoveKey}={at.ToUnixTimeSeconds()}";

            var text = $"{line1}\n{moves}";

            // BoardAtPly(-1) is the position before the first ply — for a custom game that's the board
            // the user set up (Game.SetPiece keeps it in step), which the moves are meaningless without.
            var start = game.BoardAtPly(-1);

            // The side the replay starts from, taken from the FIRST PLY'S MOVER rather than derived
            // from CurrentSide and ply parity. That derivation is wrong the moment a game ends,
            // because Game repurposes CurrentSide at that point: it holds the WINNER after checkmate
            // (see Game.Winner) and Side.None after stalemate. A game mated on an even ply therefore
            // wrote "standard board, Black to move" as its start position, and the reload rejected
            // White's opening move as illegal and discarded the entire save. Who moved first is a
            // fact that does not change when the game finishes.
            var startSide = game.PlyCount > 0
                ? start[game.Plies[0].Action.From].Side
                : game.CurrentSide;
            if (start != Board.StandardBoard || startSide != Side.White)
                text += $"\n{start.ToFEN()} {(startSide == Side.Black ? "b" : "w")}";

            File.WriteAllText(path, text);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[save] write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Line-1 token naming the other player. Its value is URL-encoded, because a display
    /// name may contain a space and line 1 is split on spaces.</summary>
    private const string OpponentKey = "opp";

    /// <summary>Line-1 token holding unix seconds when the save last changed.</summary>
    private const string LastMoveKey = "at";

    /// <summary>The value of a <c>key=value</c> token on line 1, or null when absent. Positional
    /// tokens (the computer side, the mode) carry no '=' and so can never be mistaken for one.</summary>
    private static string? ReadToken(string[] header, string key)
    {
        var prefix = key + "=";

        foreach (var token in header)
        {
            if (token.StartsWith(prefix, StringComparison.Ordinal)) return token[prefix.Length..];
        }

        return null;
    }

    // Same convention as Chess.Net's SessionProtocol, and for the same reason: a space in free text
    // would split a token. "-" stands for empty so the token never collapses to a bare "opp=".
    private static string Encode(string s) => string.IsNullOrEmpty(s) ? "-" : Uri.EscapeDataString(s);
    private static string Decode(string s) => s == "-" ? "" : Uri.UnescapeDataString(s);

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
