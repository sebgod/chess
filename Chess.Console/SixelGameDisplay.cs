using Chess.Lib.UI;
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
        // Without the reservation the encoder ranks its 255 palette slots purely by pixel count, and
        // a chess frame oversubscribes that ~8x — an accent that loses the cut snaps to the nearest
        // survivor (board tan or cream) and simply vanishes. GameUI owns the list, so a new accent
        // colour joins the palette contract in the same place it is declared.
        var renderer = new SixelRgbaImageRenderer(width, height)
        {
            ReservedColors = GameUI.ReservedPaletteColors,
        };
        return (renderer, renderer);
    }
}
