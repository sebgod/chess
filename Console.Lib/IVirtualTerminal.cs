namespace Console.Lib;

public interface IVirtualTerminal
    : ITerminalViewport, IAsyncDisposable
{
    Task InitAsync();
    bool HasSixelSupport { get; }
    (uint Width, uint Height) CellSize { get; }
    ValueTask EnterAlternateScreenAsync();
    bool IsAlternateScreen { get; }
    void Clear();
    bool HasInput();
    ConsoleInputEvent TryReadInput();
}
