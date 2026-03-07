namespace Console.Lib;

public interface ITerminalViewport
{
    (int Column, int Row) Offset { get; }
    (int Width, int Height) Size { get; }
    void SetCursorPosition(int left, int top);
    void Write(string text);
    void WriteLine(string? text = null);
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    void ResetColor();
    TermCell CellSize { get; }

    /// <summary>Viewport size in pixels (columns * cellWidth, rows * cellHeight).</summary>
    (uint Width, uint Height) PixelSize
    {
        get
        {
            var (cols, rows) = Size;
            var cell = CellSize;
            return ((uint)cols * cell.Width, (uint)rows * cell.Height);
        }
    }
    void Flush();
    Stream OutputStream { get; }
}
