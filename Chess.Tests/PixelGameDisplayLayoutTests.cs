using System.IO;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using SharpAstro.Png;
using Shouldly;
using Xunit;
using File = System.IO.File;

namespace Chess.Tests;

/// <summary>
/// Offline render tests for <see cref="PixelGameDisplay{TSurface}"/> over the CPU
/// <see cref="RgbaImageRenderer"/> (the same surface Chess.Web's <c>?renderer=cpu</c> fallback uses) —
/// no GPU/device needed. These pin the responsive layout: a portrait phone surface must actually draw
/// the board. Regression guard for the bug where the history panel width (derived from screen height)
/// exceeded a narrow screen, giving the board a NEGATIVE width so nothing painted.
/// </summary>
public sealed class PixelGameDisplayLayoutTests
{
    // The board's light squares are 0xFFCE9E and text is white; the background (#1a1a2e), history
    // panel (#202034) and status bar (#24243a) are all far darker. So a substantial fraction of
    // "light" pixels is a reliable proxy for "the board actually drew" — even the pre-fix portrait bug
    // filled the screen with the dark history-panel colour, which this predicate does NOT count.
    private static bool IsLight(byte r, byte g, byte b) => r >= 200 && g >= 170 && b >= 130;

    [Theory]
    [InlineData(1080, 2408, "portrait")]   // Samsung A14 5G — the layout that used to give a negative board width
    [InlineData(1600, 1000, "landscape")]  // desktop / web — board-left, history-right (must stay unchanged)
    public void Board_renders_on_both_orientations(int width, int height, string label)
    {
        using var renderer = new RgbaImageRenderer((uint)width, (uint)height);

        // Mimic the host's per-frame clear: PixelGameDisplay paints its chrome but relies on the host
        // to fill the base background (see PixelGameDisplay.Background). Without this the undrawn strips
        // stay transparent-black and skew a whole-image comparison.
        FillBackground(renderer.Surface.Pixels, PixelGameDisplay<RgbaImage>.Background);

        var display = new PixelGameDisplay<RgbaImage>(renderer);
        display.ResetGame(new Game());
        display.Render();

        var pixels = renderer.Surface.Pixels;
        long light = 0;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
            if (IsLight(pixels[i], pixels[i + 1], pixels[i + 2])) light++;

        var lightFraction = (double)light / ((long)width * height);

        // Emit a PNG beside the test binary so the render can be eyeballed (…-portrait.png / …-landscape.png).
        var pngPath = Path.Combine(AppContext.BaseDirectory, $"pixelgamedisplay-{label}.png");
        File.WriteAllBytes(pngPath, PngWriter.Encode(pixels, renderer.Surface.Width, renderer.Surface.Height));

        // A fully drawn board's light squares cover roughly a fifth of the surface. The pre-fix portrait
        // layout drew no board at all — essentially zero light pixels — so 5% cleanly separates the two.
        lightFraction.ShouldBeGreaterThan(0.05, $"{label} ({width}x{height}) drew too few board pixels; PNG at {pngPath}");
    }

    [Fact]
    public void Startup_menu_renders()
    {
        using var renderer = new RgbaImageRenderer(1080, 2408);
        FillBackground(renderer.Surface.Pixels, PixelGameDisplay<RgbaImage>.Background);

        // Same StartupWizard + PixelMenuWidget the Android host mounts (Chess.Droid), rendered over the
        // CPU surface — proves the menu draws without a device.
        var wizard = new StartupWizard();
        var menu = new PixelMenuWidget<RgbaImage>(renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = wizard.Current;
        menu.Reset(title, prompt, [.. items]);
        menu.Render();

        var pixels = renderer.Surface.Pixels;
        long light = 0;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
            if (IsLight(pixels[i], pixels[i + 1], pixels[i + 2])) light++;

        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "startup-menu.png"),
            PngWriter.Encode(pixels, renderer.Surface.Width, renderer.Surface.Height));

        // The title + prompt + three items are light text on the dark menu — thousands of light pixels.
        light.ShouldBeGreaterThan(2000, "startup menu drew too little text");
    }

    private static void FillBackground(byte[] px, RGBAColor32 c)
    {
        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            px[i] = c.Red;
            px[i + 1] = c.Green;
            px[i + 2] = c.Blue;
            px[i + 3] = c.Alpha;
        }
    }
}
