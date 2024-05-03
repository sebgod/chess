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
        GameStatus.Checkmate => $"Checkmate. {side} wins.",
        GameStatus.Ongoing => $"{side} to play.",
        _ => throw new ArgumentException($"Invalid game result {result}", nameof(result))
    };
}