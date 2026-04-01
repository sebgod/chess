using Console.Lib;
using DIR.Lib;

namespace Chess.Console;

/// <summary>
/// Sixel-based display using software RGBA renderer with FreeType text.
/// </summary>
internal sealed class SixelGameDisplay(IVirtualTerminal terminal) : ConsoleGameDisplayBase<RgbaImage>(terminal)
{
    protected override (Renderer<RgbaImage> Renderer, ISixelEncoder Encoder) CreateRenderer(uint width, uint height)
    {
        var renderer = new SixelRgbaImageRenderer(width, height);
        return (renderer, renderer);
    }
}
