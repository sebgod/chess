namespace Console.Lib;

public sealed class TerminalViewport : ITerminalViewport
{
    private readonly ITerminalViewport _parent;
    private int _columnOffset, _rowOffset, _width, _height;

    public TerminalViewport(ITerminalViewport parent, int columnOffset, int rowOffset, int width, int height)
    {
        _parent = parent;
        _columnOffset = columnOffset;
        _rowOffset = rowOffset;
        _width = width;
        _height = height;
        ForegroundColor = parent.ForegroundColor;
        BackgroundColor = parent.BackgroundColor;
    }

    public (int Column, int Row) Offset => (_columnOffset, _rowOffset);
    public (int Width, int Height) Size => (_width, _height);

    internal void UpdateGeometry(int columnOffset, int rowOffset, int width, int height)
    {
        _columnOffset = columnOffset;
        _rowOffset = rowOffset;
        _width = width;
        _height = height;
    }

    private void ApplyColors()
    {
        _parent.ForegroundColor = ForegroundColor;
        _parent.BackgroundColor = BackgroundColor;
    }

    public void SetCursorPosition(int left, int top)
    {
        ApplyColors();
        _parent.SetCursorPosition(
            _columnOffset + Math.Clamp(left, 0, _width - 1),
            _rowOffset + Math.Clamp(top, 0, _height - 1));
        _parent.Flush();
    }

    public void Write(string text)
    {
        ApplyColors();
        _parent.Write(text);
    }

    public void WriteLine(string? text = null)
    {
        ApplyColors();
        _parent.WriteLine(text);
    }

    public ConsoleColor ForegroundColor { get; set; }

    public ConsoleColor BackgroundColor { get; set; }

    public void ResetColor()
    {
        _parent.ResetColor();
        ForegroundColor = _parent.ForegroundColor;
        BackgroundColor = _parent.BackgroundColor;
    }

    public TermCell CellSize => _parent.CellSize;

    public void Flush() => _parent.Flush();

    public Stream OutputStream => _parent.OutputStream;
}
