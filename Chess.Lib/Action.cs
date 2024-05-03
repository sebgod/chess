using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Chess.Lib;

public readonly record struct Action(Position From, Position To, bool IsMove, PieceType Promoted = PieceType.None)
{
    public static Action DoMove(in Position from, in Position to) => new Action(from, to, IsMove: true);

    public static Action Promote(in Position from, in Position to, PieceType promoted) => new Action(from, to, IsMove: true, Promoted: promoted);

    public readonly Delta Delta => new Delta((sbyte)((int)To.File - (int)From.File), (sbyte)((int)To.Rank - (int)From.Rank));

    /// <summary>
    /// Iterates all positions in between <see cref="From"/> to <see cref="To"/>, exclusive.
    /// Jumps do not contain any points (<seealso cref="Delta.IsLShape"/>).
    /// </summary>
    /// <returns></returns>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public readonly IEnumerable<Position> Between()
    {
        var delta = Delta;

        if (delta.IsStraight)
        {
            if (delta.File is 0)
            {
                var sign = (sbyte)Math.Sign(delta.Rank);
                for (var rank = (sbyte)((int)From.Rank + sign); rank != (int)To.Rank; rank += sign)
                {
                    yield return Position.FromIndex((sbyte)From.File, rank);
                }
            }
            else
            {
                var sign = (sbyte)Math.Sign(delta.File);
                for (var file = (sbyte)((int)From.File + sign); file != (int)To.File; file += sign)
                {
                    yield return Position.FromIndex(file, (sbyte)From.Rank);
                }

            }
        }
        else if (delta.IsDiagnoal)
        {
            var fileTo = (sbyte)To.File;
            var rankTo = (sbyte)To.Rank;
            var signFile = (sbyte)Math.Sign(delta.File);
            var signRank = (sbyte)Math.Sign(delta.Rank);
            var file = (sbyte)((int)From.File + signFile);
            var rank = (sbyte)((int)From.Rank + signRank);

            for (; rank != rankTo && file != fileTo; rank += signRank, file += signFile)
            {
                yield return Position.FromIndex(file, rank);
            }
        }
        else
        {
            yield break;
        }
    }

    public override string ToString() => $"{From} {(IsMove ? "->" : "-?")} {To}";
}
