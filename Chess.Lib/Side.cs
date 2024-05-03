namespace Chess.Lib;

public enum Side : byte
{
    None,
    White,
    Black
}

public static class SideExtensions 
{
    public static Side ToOpposite(this Side side) => side switch {
        Side.White => Side.Black,
        Side.Black => Side.White,
        _ => Side.None
    };

    public static Rank HomeRank(this Side side) => side switch { Side.White => Rank.R1, Side.Black => Rank.R8, _ => throw new ArgumentException("Invalid side", nameof(side)) };

    public static Rank PawnRank(this Side side) => side switch { Side.White => Rank.R2, Side.Black => Rank.R7, _ => throw new ArgumentException("Invalid side", nameof(side)) };

    public static int PawnDirection(this Side side) => side switch { Side.White => +1, Side.Black => -1, _ => throw new ArgumentException("Invalid side", nameof(side)) };
}