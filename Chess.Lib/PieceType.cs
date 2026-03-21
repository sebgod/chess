using DIR.Lib;

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
    extension(PieceType type)
    {
        public bool IsValidPromotion => type is not PieceType.None and not PieceType.Pawn and not PieceType.King;

        public char ToUnicode(Side side) => type switch
        {
            PieceType.Pawn when side is Side.White => '\u2659',
            PieceType.Pawn when side is Side.Black => '\u265F',
            PieceType.Knight when side is Side.White => '\u2658',
            PieceType.Knight when side is Side.Black => '\u265E',
            PieceType.Bishop when side is Side.White => '\u2657',
            PieceType.Bishop when side is Side.Black => '\u265D',
            PieceType.Rook when side is Side.White => '\u2656',
            PieceType.Rook when side is Side.Black => '\u265C',
            PieceType.Queen when side is Side.White => '\u2655',
            PieceType.Queen when side is Side.Black => '\u265B',
            PieceType.King when side is Side.White => '\u2654',
            PieceType.King when side is Side.Black => '\u265A',
            _ => ' ',
        };

        public string ToPGN() => type switch
        {
            PieceType.Pawn => "",
            PieceType.Knight => "N",
            PieceType.Bishop => "B",
            PieceType.Rook => "R",
            PieceType.Queen => "Q",
            PieceType.King => "K",
            _ => throw new ArgumentException($"Unhandled piece type {type}", nameof(type))
        };
    }

    extension(PieceType)
    {
        public static PieceType? TryParseFromKey(ConsoleKey key) => key switch
        {
            ConsoleKey.P => PieceType.Pawn,
            ConsoleKey.N => PieceType.Knight,
            ConsoleKey.B => PieceType.Bishop,
            ConsoleKey.R => PieceType.Rook,
            ConsoleKey.Q or ConsoleKey.D => PieceType.Queen,
            ConsoleKey.K => PieceType.King,
            _ => null
        };

        public static PieceType? TryParseFromKey(InputKey key) => key switch
        {
            InputKey.P => PieceType.Pawn,
            InputKey.N => PieceType.Knight,
            InputKey.B => PieceType.Bishop,
            InputKey.R => PieceType.Rook,
            InputKey.Q => PieceType.Queen,
            InputKey.K => PieceType.King,
            _ => null
        };
    }
}
