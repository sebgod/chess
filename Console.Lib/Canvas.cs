namespace Console.Lib;

/// <summary>
/// A viewport-only widget for custom rendering (e.g., Sixel graphics).
/// The consumer drives rendering via <see cref="OutputStream"/> and <see cref="SetCursorPosition"/>.
/// </summary>
public class Canvas(ITerminalViewport viewport) : Widget(viewport)
{
    /// <summary>Viewport size in pixels.</summary>
    public (uint Width, uint Height) PixelSize => Viewport.PixelSize;

    /// <summary>Raw output stream for writing binary data (e.g., Sixel).</summary>
    public Stream OutputStream => Viewport.OutputStream;

    /// <summary>Position the cursor within the canvas.</summary>
    public void SetCursorPosition(int col, int row) => Viewport.SetCursorPosition(col, row);

    /// <summary>No-op: custom rendering is handled by the consumer.</summary>
    public override void Render() { }
}
