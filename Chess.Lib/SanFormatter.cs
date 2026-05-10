using System.Collections.Immutable;
using System.Text;

namespace Chess.Lib;

public static class SanFormatter
{
    /// <summary>
    /// Converts an <see cref="Action"/> into Standard Algebraic Notation (e.g. "Bd5#", "exd6", "O-O-O+").
    /// Includes minimal disambiguation, capture mark, promotion suffix, and check/mate suffix.
    /// </summary>
    public static string ToSan(this Action action, Board board, ImmutableList<RecordedPly> plies)
    {
        var pieceFrom = board[action.From];
        if (pieceFrom == Piece.None)
        {
            // Fall back to UCI-ish for invalid input rather than throwing.
            return $"{action.From}{action.To}";
        }

        var ((result, status), _, _) = board.EvaluateAction(plies, action);

        var statusSuffix = status switch
        {
            GameStatus.Check => "+",
            GameStatus.Checkmate => "#",
            _ => ""
        };

        if (result is ActionResult.Castling)
        {
            return string.Concat(action.To.File == File.C ? "O-O-O" : "O-O", statusSuffix);
        }

        var sb = new StringBuilder(8);
        var moved = pieceFrom.PieceType;
        var isCapture = result.IsCapture() || (moved is PieceType.Pawn && action.From.File != action.To.File);

        if (moved is PieceType.Pawn)
        {
            if (isCapture)
            {
                sb.Append(action.From.File.ToLabel());
            }
        }
        else
        {
            sb.Append(moved.ToPGN());
            sb.Append(Disambiguate(board, plies, action, pieceFrom));
        }

        if (isCapture)
        {
            sb.Append('x');
        }

        sb.Append(action.To);

        if (result.IsPromotion() && action.Promoted.IsValidPromotion)
        {
            sb.Append('=').Append(action.Promoted.ToPGN());
        }

        sb.Append(statusSuffix);
        return sb.ToString();
    }

    private static string Disambiguate(Board board, ImmutableList<RecordedPly> plies, Action action, Piece pieceFrom)
    {
        var moved = pieceFrom.PieceType;
        if (moved is PieceType.King)
        {
            return string.Empty;
        }

        var sameFile = false;
        var sameRank = false;
        var anyOther = false;

        foreach (var (otherPos, otherPiece) in board.AllPiecesOfSide(pieceFrom.Side))
        {
            if (otherPos == action.From || otherPiece.PieceType != moved)
            {
                continue;
            }

            var otherAction = action.Promoted.IsValidPromotion
                ? Action.Promote(otherPos, action.To, action.Promoted)
                : Action.DoMove(otherPos, action.To);

            var ((otherResult, _), _, _) = board.EvaluateAction(plies, otherAction, skipGameResultCheck: true);
            if (!otherResult.IsMoveOrCapture())
            {
                continue;
            }

            anyOther = true;
            if (otherPos.File == action.From.File) sameFile = true;
            if (otherPos.Rank == action.From.Rank) sameRank = true;
        }

        if (!anyOther) return string.Empty;
        if (!sameFile) return action.From.File.ToLabel();
        if (!sameRank) return action.From.Rank.ToLabel();
        return action.From.ToString();
    }
}
