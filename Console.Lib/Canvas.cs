using DIR.Lib;

namespace Console.Lib;

/// <summary>
/// A widget that renders a <see cref="SixelRenderer{TSurface}"/> to a viewport.
/// <see cref="Widget.Render()"/> performs a full Sixel blit;
/// <see cref="Render(RectInt)"/> renders only the dirty region.
/// </summary>
public class Canvas<TSurface>(ITerminalViewport viewport, SixelRenderer<TSurface> renderer) : Widget(viewport)
{
    /// <summary>Viewport size in pixels.</summary>
    public (uint Width, uint Height) PixelSize => Viewport.PixelSize;

    /// <summary>The renderer that owns the drawing surface.</summary>
    public SixelRenderer<TSurface> Renderer => renderer;

    /// <summary>Position the cursor within the canvas.</summary>
    public void SetCursorPosition(int col, int row) => Viewport.SetCursorPosition(col, row);

    /// <summary>
    /// Renders a partial Sixel update for the given dirty region.
    /// The clip rectangle's Y bounds (in pixels) are aligned to cell-height
    /// boundaries before encoding, since Sixel output must start at a character row.
    /// </summary>
    /// <param name="clip">Dirty region in pixel coordinates.</param>
    public void Render(RectInt clip)
    {
        var cellHeight = Viewport.CellSize.Height;
        var startRow = clip.UpperLeft.Y / cellHeight;
        var endRow = (clip.LowerRight.Y + cellHeight - 1) / cellHeight;

        var pixelStartY = startRow * cellHeight;
        var pixelEndY = (int)Math.Min(renderer.Height, (uint)(endRow * cellHeight));
        var cropHeight = pixelEndY - pixelStartY;

        if (cropHeight > 0)
        {
            SetCursorPosition(0, startRow);
            renderer.EncodeSixel(pixelStartY, (uint)cropHeight, Viewport.OutputStream);
        }
    }

    /// <summary>Performs a full Sixel blit of the renderer surface.</summary>
    public override void Render()
    {
        SetCursorPosition(0, 0);
        renderer.EncodeSixel(Viewport.OutputStream);
    }
}
