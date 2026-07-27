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
    /// <summary>
    /// A search request. All times are milliseconds. <see cref="WTime"/>/<see cref="BTime"/> are what
    /// each side has left on the clock and <see cref="WInc"/>/<see cref="BInc"/> the per-move
    /// increment; <see cref="MovesToGo"/> is the number of moves to the next time control, absent for
    /// sudden death. Turning these into an actual budget is <see cref="UciTimeBudget"/>'s job.
    /// </summary>
    public sealed record Go(
        int? MoveTime = null,
        int? Depth = null,
        bool Infinite = false,
        int? WTime = null,
        int? BTime = null,
        int? WInc = null,
        int? BInc = null,
        int? MovesToGo = null) : UciCommand;
    public sealed record Stop : UciCommand;
    public sealed record Quit : UciCommand;
    public sealed record Debug(bool On) : UciCommand;
}
