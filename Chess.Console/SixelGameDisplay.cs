using Console.Lib;
using DIR.Lib;

namespace Chess.Console;

/// <summary>
/// Sixel-based display using software RGBA renderer with FreeType text.
/// </summary>
internal sealed class SixelGameDisplay(IVirtualTerminal terminal) : ConsoleGameDisplayBase<RgbaImage>(terminal)
{
    protected override SixelRenderer<RgbaImage> CreateRenderer(uint width, uint height)
        => new RgbaImageRenderer(width, height);
}
