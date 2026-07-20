using Chess.Lib;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.UCI;

/// <summary>
/// Converts between UCI move strings (e.g. "e2e4", "e7e8q") and Chess.Lib <see cref="Action"/>.
/// </summary>
public static class UciMove
{
    public static Action Parse(string move)
    {
        if (move.Length < 4 || move.Length > 5)
            throw new FormatException($"Invalid UCI move string: '{move}'");

        var fromFile = ParseFile(move[0]);
        var fromRank = ParseRank(move[1]);
        var toFile = ParseFile(move[2]);
        var toRank = ParseRank(move[3]);

        var from = new Position(fromFile, fromRank);
        var to = new Position(toFile, toRank);

        if (move.Length == 5)
        {
            var promoted = ParsePromotion(move[4]);
            return Action.Promote(from, to, promoted);
        }

        return Action.DoMove(from, to);
    }

    public static string Format(Action action)
    {
        var result = action.From.ToString() + action.To.ToString();

        if (action.Promoted is not PieceType.None)
        {
            result += FormatPromotion(action.Promoted);
        }

        return result;
    }

    /// <summary>
    /// Formats a single recorded ply as a UCI move string, preserving the promotion piece.
    /// <see cref="RecordedPly.Action"/> drops <c>Promoted</c>, so a promotion is rebuilt via the
    /// factory here — otherwise "e7e8q" would silently degrade to "e7e8".
    /// </summary>
    public static string FormatPly(RecordedPly ply) =>
        Format(ply.Promoted is not PieceType.None
            ? Action.Promote(ply.From, ply.To, ply.Promoted)
            : Action.DoMove(ply.From, ply.To));

    /// <summary>
    /// Formats a game's played plies as UCI move strings, in order — the one shared "moves" list
    /// every mover-serializer needs: engine <c>position … moves …</c> sync, Play-by-Link fragments,
    /// the Continue save, and LAN move exchange. Each move keeps its promotion piece via
    /// <see cref="FormatPly"/>.
    /// </summary>
    public static string[] FormatMoves(Game game)
    {
        var plies = game.Plies;
        var moves = new string[plies.Count];
        for (var i = 0; i < plies.Count; i++)
        {
            moves[i] = FormatPly(plies[i]);
        }

        return moves;
    }

    private static File ParseFile(char c) => c switch
    {
        >= 'a' and <= 'h' => (File)(c - 'a'),
        _ => throw new FormatException($"Invalid file character: '{c}'")
    };

    private static Rank ParseRank(char c) => c switch
    {
        >= '1' and <= '8' => (Rank)(c - '1'),
        _ => throw new FormatException($"Invalid rank character: '{c}'")
    };

    private static PieceType ParsePromotion(char c) => c switch
    {
        'q' => PieceType.Queen,
        'r' => PieceType.Rook,
        'b' => PieceType.Bishop,
        'n' => PieceType.Knight,
        _ => throw new FormatException($"Invalid promotion character: '{c}'")
    };

    private static char FormatPromotion(PieceType type) => type switch
    {
        PieceType.Queen => 'q',
        PieceType.Rook => 'r',
        PieceType.Bishop => 'b',
        PieceType.Knight => 'n',
        _ => throw new ArgumentException($"Invalid promotion type: {type}", nameof(type))
    };
}
