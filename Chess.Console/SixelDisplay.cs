using Chess.Lib.UI;
using Console.Lib;
using ImageMagick;
using System.Collections.Immutable;

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
internal sealed class SixelDisplay(ITerminalViewport viewport)
{
#if DEBUG
    private readonly Stopwatch _stopwatch = new();
    private double _lastFrameMs;
    private long _fullRenders;
    private long _partialRenders;
#endif
    private readonly byte _cellHeight = viewport.CellSize.Height;

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
    public void RenderFrame(GameUI ui, MagickImageRenderer renderer,
        ImmutableArray<RectInt> clipRects)
    {
#if DEBUG
        _stopwatch.Restart();
#endif

        var image = renderer.Surface;
        RectInt clip;
        bool isFullRender;
        if (!clipRects.IsDefault && clipRects.Length > 0)
        {
            isFullRender = false;
            clip = clipRects[0];
            for (var i = 1; i < clipRects.Length; i++)
            {
                clip = clip.Union(clipRects[i]);
            }
        }
        else
        {
            isFullRender = true;
            clip = new RectInt((renderer.Width, renderer.Height), PointInt.Origin);
        }

        ui.Render<MagickImage, MagickImageRenderer>(renderer, clip);

        if (isFullRender)
        {
            viewport.SetCursorPosition(0, 0);
            image.EncodeSixel(viewport.OutputStream);
        }
        else
        {
            // Align to cell boundaries for proper cursor positioning
            var startRow = clip.UpperLeft.Y / _cellHeight;
            var endRow = (clip.LowerRight.Y + _cellHeight - 1) / _cellHeight;

            var pixelStartY = startRow * _cellHeight;
            var pixelEndY = Math.Min(renderer.Height, endRow * _cellHeight);
            var cropHeight = pixelEndY - pixelStartY;

            if (cropHeight > 0)
            {
                viewport.SetCursorPosition(0, startRow);
                image.EncodeSixel(pixelStartY, (uint)cropHeight, viewport.OutputStream);
            }
        }

#if DEBUG
        _stopwatch.Stop();
        _lastFrameMs = _stopwatch.Elapsed.TotalMilliseconds;
        if (isFullRender) _fullRenders++; else _partialRenders++;
#endif
    }
}
