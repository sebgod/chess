using System.Collections.Immutable;
using System.Text;

namespace Chess.Lib;

public readonly record struct RecordedPly(Position From, Position To, ActionResult Result, PieceType Moved, PieceType CapturedOrPromoted = PieceType.None, GameStatus Status = GameStatus.Ongoing)
{
    public RecordedPly(Action action, ActionResult result, in Piece from, in Piece to, GameStatus status = GameStatus.Ongoing)
        : this(action.From, action.To, result, from.PieceType, to.PieceType, status)
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
                Result switch { ActionResult.Capture => "x", _ => ""
                },
                To,
                Result switch { ActionResult.Promotion => $"={CapturedOrPromoted}", ActionResult.EnPassant => " e.p.", _ => "" },
                status
            );
        }
    }
}

public static class RecordedPlyExtensions
{
    public static string ToPGN(this ImmutableList<RecordedPly> plies)
    {
        var sb = new StringBuilder(plies.Count * 10);

        for (var i = 0; i < plies.Count; i++)
        {
            if (i % 2 == 0)
            {
                sb.AppendFormat("{0,4}", $"{(i / 2) + 1}.").Append(' ');
            }

            sb.Append(plies[i]).Append(' ');
        }

        return sb.ToString().TrimEnd();
    }
}