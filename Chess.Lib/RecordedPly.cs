using System.Collections.Immutable;
using System.Text;

namespace Chess.Lib;

public readonly record struct RecordedPly(Position From, Position To, ActionResult Result, PieceType Moved, PieceType Captured = PieceType.None, PieceType Promoted = PieceType.None, GameStatus Status = GameStatus.Ongoing)
{
    public RecordedPly(Action action, ActionResult result, in Piece from, in Piece to, PieceType promoted = PieceType.None, GameStatus status = GameStatus.Ongoing)
        : this(action.From, action.To, result, from.PieceType, to.PieceType, promoted, status)
    {
        // calls base
    }

    public readonly Action Action => new Action(From, To, IsMove: Result.IsMoveOrCapture());

    public override readonly string ToString()
    {
        var status = Status switch { GameStatus.Check => "+", GameStatus.Checkmate => "#", _ => "" };

        if (Result is ActionResult.Castling)
        {
            return string.Concat(To.File == File.C ? "O-O-O" : "O-O", status);
        }
        else
        {
            return string.Concat(
                Moved.ToPGN(),
                From,
                Result switch { ActionResult.Capture or ActionResult.CaptureAndPromotion => "x", _ => ""
                },
                To,
                Result switch { ActionResult.Promotion or ActionResult.CaptureAndPromotion => $"={Promoted.ToPGN()}", ActionResult.EnPassant => " e.p.", _ => "" },
                status
            );
        }
    }
}

public static class RecordedPlyExtensions
{
    public static string ToPGN(this ImmutableList<RecordedPly> plies)
    {
        var sb = new StringBuilder(plies.Count * 20);

        for (var i = 0; i < plies.Count; i++)
        {
            var (idxStr, ply) = plies.GetRecordAndPGNIdx(i);
            if (i % 2 == 0)
            {
                sb.Append(idxStr).Append(' ');
            }

            sb.Append(ply).Append(' ');
        }

        return sb.ToString().TrimEnd();
    }

    public static (string Idx, RecordedPly Ply) GetRecordAndPGNIdx(this ImmutableList<RecordedPly> plies, int index)
    {
        var idxStr = string.Format("{0,4}{1}", (index / 2) + 1, index % 2 == 0 ? "." : "...");
        return (idxStr, plies[index]);
    }
}