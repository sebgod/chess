namespace Console.Lib;

public interface ITerminalViewport
{
    (int Width, int Height) Size { get; }
    void SetCursorPosition(int left, int top);
    void Write(string text);
    void WriteLine(string? text = null);
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    void ResetColor();
    void Flush();
    Stream OutputStream { get; }
}
