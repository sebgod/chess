namespace Console.Lib;

public sealed class TerminalViewport(ITerminalViewport parent, int columnOffset, int rowOffset, int width, int height) : ITerminalViewport
{
    private int _columnOffset = columnOffset, _rowOffset = rowOffset, _width = width, _height = height;

    public (int Column, int Row) Offset => (_columnOffset, _rowOffset);
    public (int Width, int Height) Size => (_width, _height);

    internal void UpdateGeometry(int columnOffset, int rowOffset, int width, int height)
    {
        _columnOffset = columnOffset;
        _rowOffset = rowOffset;
        _width = width;
        _height = height;
    }

    public void SetCursorPosition(int left, int top)
    {
        parent.SetCursorPosition(
            _columnOffset + Math.Clamp(left, 0, _width - 1),
            _rowOffset + Math.Clamp(top, 0, _height - 1));
        parent.Flush();
    }

    public void Write(string text) => parent.Write(text);

    public void WriteLine(string? text = null) => parent.WriteLine(text);

    public TermCell CellSize => parent.CellSize;

    public void Flush() => parent.Flush();

    public Stream OutputStream => parent.OutputStream;

    public ColorMode ColorMode => parent.ColorMode;
}
