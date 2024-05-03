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

    public override readonly string ToString() => string.Concat(
        From, 
        To,
        Result switch { ActionResult.Promotion => $"={CapturedOrPromoted}", ActionResult.Capture => "x", _ => "" },
        Status switch { GameStatus.Check => "+", GameStatus.Checkmate => "#", _ => "" }
    );

    public static string ToPGN(ImmutableList<RecordedPly> plies)
    {
        var sb = new StringBuilder(plies.Count * 10);

        for (var i = 0; i < plies.Count; i++)
        {
            if (i % 2 == 0)
            {
                sb.Append(i + 1).Append(". ");
                sb.Append(plies[i]).Append(' ');
            }
        }

        return sb.ToString().TrimEnd();
    }
}