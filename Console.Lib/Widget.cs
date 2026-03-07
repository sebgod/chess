namespace Console.Lib;

/// <summary>
/// Base class for terminal widgets that render content to a viewport.
/// </summary>
public abstract class Widget
{
    /// <summary>
    /// The viewport this widget renders to. Assigned by <see cref="Panel"/>.
    /// </summary>
    public ITerminalViewport Viewport { get; internal set; } = null!;

    /// <summary>
    /// Render the widget's current state to its viewport.
    /// </summary>
    public abstract void Render();

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
