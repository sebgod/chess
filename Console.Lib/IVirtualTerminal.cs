namespace Console.Lib;

public interface IVirtualTerminal
    : IAsyncDisposable
{
    Task<bool> HasSixelSupportAsync();
    Task<(uint Width, uint Height)?> QueryCellSizeAsync();
    ValueTask EnterAlternateScreenAsync();
    bool IsAlternateScreen { get; }
    (int Width, int Height) Size { get; }
    void Clear();
    void SetCursorPosition(int left, int top);
    void Write(string text);
    void WriteLine(string text = "");
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    void ResetColor();
    void Flush();
    Stream OutputStream { get; }
    bool HasInput();
    ConsoleInputEvent TryReadInput();
}
