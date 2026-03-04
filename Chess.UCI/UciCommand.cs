namespace Chess.UCI;

/// <summary>
/// Represents UCI commands sent from GUI to Engine.
/// </summary>
public abstract record UciCommand
{
    public sealed record UciInit : UciCommand;
    public sealed record IsReady : UciCommand;
    public sealed record UciNewGame : UciCommand;
    public sealed record SetPosition(string? Fen, string[] Moves) : UciCommand;
    public sealed record Go(int? MoveTime = null, int? Depth = null, bool Infinite = false, int? WTime = null, int? BTime = null) : UciCommand;
    public sealed record Stop : UciCommand;
    public sealed record Quit : UciCommand;
    public sealed record Debug(bool On) : UciCommand;
}
