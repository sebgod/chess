using Chess.Lib.UI;
using ImageMagick;

namespace Chess.Console;

/// <summary>
/// Renders a <see cref="GameUI"/> scene to the terminal using Sixel graphics,
/// supporting both full and partial (clip-region) output.
/// </summary>
internal sealed class SixelDisplay : IDisposable
{
    private readonly Stream _stdout = System.Console.OpenStandardOutput();

    /// <summary>
    /// Renders the UI scene to the terminal, optionally restricted to the given clip rectangles.
    /// </summary>
    public void RenderFrame(GameUI ui, MagickImageRenderer renderer, MagickImage image,
        IReadOnlyList<RectInt>? clipRects, int cellHeight)
    {
        RectInt clip;
        bool isFullRender;
        if (clipRects is { Count: > 0 })
        {
            isFullRender = false;
            clip = clipRects[0];
            for (var i = 1; i < clipRects.Count; i++)
            {
                clip = clip.Union(clipRects[i]);
            }
        }
        else
        {
            isFullRender = true;
            clip = new RectInt((image.Width, image.Height), (0, 0));
        }

        ui.Render(renderer, image, clip);

        if (isFullRender)
        {
            System.Console.SetCursorPosition(0, 0);
            System.Console.Out.Flush();
            image.Write(_stdout, MagickFormat.Sixel);
        }
        else
        {
            // Align to cell boundaries for proper cursor positioning
            var startRow = (int)(clip.UpperLeft.Y / cellHeight);
            var endRow = (int)((clip.LowerRight.Y + cellHeight - 1) / cellHeight);

            var pixelStartY = startRow * cellHeight;
            var pixelEndY = Math.Min((int)image.Height, endRow * cellHeight);
            var cropHeight = pixelEndY - pixelStartY;

            if (cropHeight > 0)
            {
                using var cropped = image.Clone();
                cropped.Crop(new MagickGeometry(0, pixelStartY, image.Width, (uint)cropHeight));
                cropped.ResetPage();

                System.Console.SetCursorPosition(0, startRow);
                System.Console.Out.Flush();
                cropped.Write(_stdout, MagickFormat.Sixel);
            }
        }
    }

    public void Dispose() => _stdout.Dispose();
}
