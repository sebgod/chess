namespace Chess.Lib;

// See https://aka.ms/new-console-template for more information
public enum PieceType : byte
{
    None,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King
}

public static class PieceTypeExtensions
{
    public static bool IsValidPromotion(this PieceType type) => type is not PieceType.None and not PieceType.Pawn and not PieceType.King;

    public static char ToUnicode(this PieceType type, Side side) => type switch
    {
        PieceType.Pawn   when side is Side.White => '\u2659',
        PieceType.Pawn   when side is Side.Black => '\u265F',
        PieceType.Knight when side is Side.White => '\u2658',
        PieceType.Knight when side is Side.Black => '\u265E',
        PieceType.Bishop when side is Side.White => '\u2657',
        PieceType.Bishop when side is Side.Black => '\u265D',
        PieceType.Rook   when side is Side.White => '\u2656',
        PieceType.Rook   when side is Side.Black => '\u265C',
        PieceType.Queen  when side is Side.White => '\u2655',
        PieceType.Queen  when side is Side.Black => '\u265B',
        PieceType.King   when side is Side.White => '\u2654',
        PieceType.King   when side is Side.Black => '\u265A',
        _ => ' ',
    };
}
