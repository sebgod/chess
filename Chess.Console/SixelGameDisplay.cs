using Chess.ImageMagick;
using Console.Lib;
using ImageMagick;

namespace Chess.Console;

/// <summary>
/// Sixel-based display using ImageMagick for rendering.
/// </summary>
internal sealed class SixelGameDisplay(IVirtualTerminal terminal) : ConsoleGameDisplayBase<MagickImage>(terminal)
{
    protected override SixelRenderer<MagickImage> CreateRenderer(uint width, uint height)
        => new MagickImageRenderer(width, height);
}
