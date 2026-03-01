namespace Chess.Lib;

public enum GameStatus : byte
{
    Ongoing,
    Check,
    Checkmate,
    Stalemate
}

public static class GameStatusExtensions
{
    public static Side WhoWins(this GameStatus result, Side current) => result switch
    {
        GameStatus.Checkmate => current,
        _ => Side.None
    };

    public static string ToMessage(this GameStatus result, Side side) => result switch
    {
        GameStatus.Stalemate => "Stalemate. Game is draw.",
        GameStatus.Checkmate when side is Side.White => "Checkmate. White wins.",
        GameStatus.Checkmate when side is Side.Black => "Checkmate. Black wins.",
        GameStatus.Ongoing when side is Side.White => "White to move.",
        GameStatus.Ongoing when side is Side.Black => "Black to move.",
        GameStatus.Check when side is Side.White => "White king is in check.",
        GameStatus.Check when side is Side.Black => "Black king is in check.",
        _ => throw new ArgumentException($"Invalid game result {result}", nameof(result))
    };
}