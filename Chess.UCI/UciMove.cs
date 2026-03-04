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
