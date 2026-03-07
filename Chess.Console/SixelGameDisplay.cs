using Chess.ImageMagick;
using DIR.Lib;
using Console.Lib;
using ImageMagick;

namespace Chess.Console;

/// <summary>
/// Sixel-based display using ImageMagick for rendering.
/// </summary>
internal sealed class SixelGameDisplay(IVirtualTerminal terminal) : ConsoleGameDisplayBase<MagickImage>(terminal)
{
    protected override Renderer<MagickImage> CreateRenderer(uint width, uint height)
        => new MagickImageRenderer(width, height);

    protected override void EncodeSixel(MagickImage surface, Stream output)
        => surface.EncodeSixel(output);

    protected override void EncodeSixel(MagickImage surface, int startY, uint height, Stream output)
        => surface.EncodeSixel(startY, height, output);
}
