namespace Chess.Lib;

public readonly record struct Piece(PieceType PieceType, Side Side)
{
    public static readonly Piece None = new Piece();

    public override readonly string ToString() => char.ToString(PieceType.ToUnicode(Side));

    public readonly char ToFEN() => PieceType switch {
        PieceType.Pawn when Side is Side.Black => 'p',
        PieceType.Knight when Side is Side.Black => 'n',
        PieceType.Bishop when Side is Side.Black => 'b',
        PieceType.Rook when Side is Side.Black => 'r',
        PieceType.Queen when Side is Side.Black => 'q',
        PieceType.King when Side is Side.Black => 'k',
        PieceType.Pawn when Side is Side.White => 'P',
        PieceType.Knight when Side is Side.White => 'N',
        PieceType.Bishop when Side is Side.White => 'B',
        PieceType.Rook when Side is Side.White => 'R',
        PieceType.Queen when Side is Side.White => 'Q',
        PieceType.King when Side is Side.White => 'K',
        _ => throw new InvalidOperationException($"Unknown piece {PieceType} of side {Side}")
    };

    public static implicit operator Piece((Side Side, PieceType PieceType) Pair) => new Piece(Pair.PieceType, Pair.Side);
}