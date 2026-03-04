namespace Chess.UCI;

/// <summary>
/// Represents UCI responses sent from Engine to GUI.
/// </summary>
public abstract record UciResponse
{
    public sealed record Id(string Type, string Value) : UciResponse;
    public sealed record UciOk : UciResponse;
    public sealed record ReadyOk : UciResponse;
    public sealed record BestMove(string Move, string? Ponder = null) : UciResponse;
    public sealed record Info(string Message) : UciResponse;
}
