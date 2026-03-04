namespace Chess.UCI;

/// <summary>
/// Interface for UCI engine implementations.
/// </summary>
public interface IUciEngine
{
    void OnUci(TextWriter output);
    void OnIsReady(TextWriter output);
    void OnNewGame();
    void OnPosition(string? fen, string[] moves);
    void OnGo(UciCommand.Go goParams, TextWriter output);
    void OnStop(TextWriter output);
    void OnDebug(bool on);
}
