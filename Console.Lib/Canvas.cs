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

    /// <summary>
    /// Writes Sixel data to the output stream with correct cursor positioning.
    /// For a full blit (clip spans entire height), encodes the full surface.
    /// For a partial blit, aligns to cell-height boundaries and encodes a cropped slice.
    /// </summary>
    public void BlitSixel<TSurface>(SixelRenderer<TSurface> renderer, int clipUpperY, int clipLowerY)
    {
        if (clipUpperY <= 0 && clipLowerY >= (int)renderer.Height)
        {
            SetCursorPosition(0, 0);
            renderer.EncodeSixel(OutputStream);
        }
        else
        {
            var cellHeight = Viewport.CellSize.Height;
            var startRow = clipUpperY / cellHeight;
            var endRow = (clipLowerY + cellHeight - 1) / cellHeight;

            var pixelStartY = startRow * cellHeight;
            var pixelEndY = (int)Math.Min(renderer.Height, (uint)(endRow * cellHeight));
            var cropHeight = pixelEndY - pixelStartY;

            if (cropHeight > 0)
            {
                SetCursorPosition(0, startRow);
                renderer.EncodeSixel(pixelStartY, (uint)cropHeight, OutputStream);
            }
        }
    }

    /// <summary>No-op: custom rendering is handled by the consumer.</summary>
    public override void Render() { }
}
