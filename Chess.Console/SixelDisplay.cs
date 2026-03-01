using Chess.Lib.UI;
using ImageMagick;
#if DEBUG
using System.Diagnostics;
#endif

namespace Chess.Console;

/// <summary>
/// Snapshot of rendering performance counters.
/// </summary>
internal readonly record struct RenderStats(double LastFrameMs, long FullRenders, long PartialRenders);

/// <summary>
/// Renders a <see cref="GameUI"/> scene to the terminal using Sixel graphics,
/// supporting both full and partial (clip-region) output.
/// </summary>
internal sealed class SixelDisplay : IDisposable
{
    private readonly Stream _stdout = System.Console.OpenStandardOutput();

#if DEBUG
    private readonly Stopwatch _stopwatch = new();
    private double _lastFrameMs;
    private long _fullRenders;
    private long _partialRenders;
#endif

    /// <summary>
    /// Returns the latest rendering performance counters, or null in Release builds.
    /// </summary>
    public RenderStats? Stats =>
#if DEBUG
        new(_lastFrameMs, _fullRenders, _partialRenders);
#else
        null;
#endif

    /// <summary>
    /// Renders the UI scene to the terminal, optionally restricted to the given clip rectangles.
    /// </summary>
    public void RenderFrame(GameUI ui, MagickImageRenderer renderer, MagickImage image,
        IReadOnlyList<RectInt>? clipRects, int cellHeight)
    {
#if DEBUG
        _stopwatch.Restart();
#endif

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
            SixelEncoder.Encode(image, _stdout);
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
                System.Console.SetCursorPosition(0, startRow);
                System.Console.Out.Flush();
                SixelEncoder.Encode(image, pixelStartY, (uint)cropHeight, _stdout);
            }
        }

#if DEBUG
        _stopwatch.Stop();
        _lastFrameMs = _stopwatch.Elapsed.TotalMilliseconds;
        if (isFullRender) _fullRenders++; else _partialRenders++;
#endif
    }

    public void Dispose() => _stdout.Dispose();
}
