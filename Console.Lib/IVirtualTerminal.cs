namespace Console.Lib;

public interface IVirtualTerminal
    : ITerminalViewport, IAsyncDisposable
{
    Task InitAsync();
    bool HasSixelSupport { get; }
    void EnterAlternateScreen();
    bool IsAlternateScreen { get; }
    void Clear();
    bool HasInput();
    ConsoleInputEvent TryReadInput();
}
