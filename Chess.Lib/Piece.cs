using System.Diagnostics;

namespace Chess.Lib;

public readonly record struct Piece(PieceType PieceType, Side Side)
{
    public static readonly Piece None = new Piece();

    public override readonly string ToString() => char.ToString(PieceType.ToUnicode(Side));

    public static implicit operator Piece((Side Side, PieceType PieceType) Pair) => new Piece(Pair.PieceType, Pair.Side);
}