namespace Console.Lib;

/// <summary>
/// Base class for terminal widgets that render content to a viewport.
/// </summary>
public abstract class Widget(ITerminalViewport viewport)
{
    /// <summary>
    /// The viewport this widget renders to.
    /// </summary>
    public ITerminalViewport Viewport { get; } = viewport;

    /// <summary>
    /// Render the widget's current state to its viewport.
    /// </summary>
    public abstract void Render();

    /// <summary>
    /// Converts absolute pixel coordinates to viewport-local cell coordinates.
    /// Returns <c>null</c> if the point is outside this widget's viewport.
    /// </summary>
    public (int Col, int Row)? HitTest(int pixelX, int pixelY)
    {
        var cell = Viewport.CellSize;
        var col = pixelX / cell.Width - Viewport.Offset.Column;
        var row = pixelY / cell.Height - Viewport.Offset.Row;
        var (w, h) = Viewport.Size;
        if (col < 0 || col >= w || row < 0 || row >= h)
            return null;
        return (col, row);
    }

    protected static bool TrySetCursorPosition(ITerminalViewport viewport, int left, int top)
    {
        try
        {
            viewport.SetCursorPosition(left, top);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
