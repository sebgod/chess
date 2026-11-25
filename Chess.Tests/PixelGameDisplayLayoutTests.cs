using System.IO;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using SharpAstro.Png;
using Shouldly;
using Xunit;
using File = System.IO.File;
using Layout = DIR.Lib.Layout;

namespace Chess.Tests;

/// <summary>
/// Offline render tests for <see cref="PixelGameDisplay{TSurface}"/> over the CPU
/// <see cref="RgbaImageRenderer"/> — no GPU/device needed, which is also what keeps the software
/// rasterizer an honest A/B reference for the GPU front-ends now that Chess.Web has none of its own
/// (its <c>?renderer=cpu</c> path is gone; this is where that comparison lives).
/// These pin the responsive layout: a portrait phone surface must actually draw
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
    [InlineData(2360, 1640, "ipad-landscape")] // iPad Air 11" (iPad15,3), the iOS head's first device
    [InlineData(1640, 2360, "ipad-portrait")]
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

    // PixelGameDisplay.StatusBarBg — the chrome bar fill, used by both the status bar and the notch
    // stats strip. Unique among the display's colors, so counting it per row-band locates the chrome.
    private static bool IsChromeBar(byte r, byte g, byte b) => r == 0x24 && g == 0x24 && b == 0x3a;

    [Fact]
    public void Safe_area_insets_move_chrome_clear_of_notch_and_gesture_bar()
    {
        // A14-ish portrait with a punch-hole top inset and a gesture-bar bottom inset. Pins the
        // safe-area layout: the notch row becomes a stats strip, the status bar rises above the
        // gesture band, and the gesture band itself stays chrome-free.
        const int W = 1080, H = 2408, Top = 100, Bottom = 60;
        using var renderer = new RgbaImageRenderer(W, H);
        FillBackground(renderer.Surface.Pixels, PixelGameDisplay<RgbaImage>.Background);

        var display = new PixelGameDisplay<RgbaImage>(renderer)
        {
            SafeAreaInsets = (0, Top, 0, Bottom),
            TopStripLabel = "You (White) vs AI",
        };
        display.ResetGame(new Game());
        display.Render();

        var px = renderer.Surface.Pixels;
        long ChromeBarIn(int rowStart, int rowEnd)
        {
            long n = 0;
            for (var i = rowStart * W * 4; i < rowEnd * W * 4; i += 4)
                if (IsChromeBar(px[i], px[i + 1], px[i + 2])) n++;
            return n;
        }

        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "pixelgamedisplay-insets.png"),
            PngWriter.Encode(px, renderer.Surface.Width, renderer.Surface.Height));

        // The notch row is painted as a chrome bar (pre-fix: raw background under the cutout).
        ChromeBarIn(0, Top).ShouldBeGreaterThan(40_000, "notch strip not painted");
        // The gesture-bar band is chrome-free (pre-fix: the status bar sat flush at the bottom).
        ChromeBarIn(H - Bottom, H).ShouldBe(0, "chrome drawn under the gesture bar");
        // The status bar sits directly above the gesture band.
        ChromeBarIn(H - Bottom - 130, H - Bottom).ShouldBeGreaterThan(40_000, "status bar not above the inset");

        long light = 0;
        for (var i = 0; i + 3 < px.Length; i += 4)
            if (IsLight(px[i], px[i + 1], px[i + 2])) light++;
        ((double)light / ((long)W * H)).ShouldBeGreaterThan(0.05, "board did not render with insets");
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

    /// <summary>
    /// Records the geometry of every text run the display hands the renderer — the string, the size it was
    /// finally drawn at (which the painter may have reduced to fit), and the rect it was drawn into. Drawing
    /// is skipped; measuring goes through the real font, so widths are the ones the app produces.
    /// </summary>
    private sealed class RunRecorder(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<(string Text, string Font, float FontSize, RectInt Rect)> Runs { get; } = [];

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
            => Runs.Add((text.ToString(), fontFamily, fontSize, layout));
    }

    /// <summary>Two moves deep, including the longest notation the history draws (<c>Nc6xb4</c>).</summary>
    private static Game TwoMovesWithACapture()
    {
        var game = new Game();
        game.TryMove(Position.B1, Position.A3);
        game.TryMove(Position.B8, Position.C6);
        game.TryMove(Position.B2, Position.B4);
        game.TryMove(Position.C6, Position.B4);
        return game;
    }

    /// <summary>
    /// Renders a playback frame (so the header carries its "▶ Latest" chip) and hands back both what reached
    /// the renderer and the display itself.
    /// </summary>
    private static (RunRecorder Renderer, PixelGameDisplay<RgbaImage> Display, Game Game) RenderPlayback(
        int width, int height)
    {
        // The arranged tree is readable back through GetCapturedLayout() with nothing to switch on: DIR.Lib
        // 8.8 made layout capture unconditional, because the tree is what LayoutDamage diffs a frame against.
        var renderer = new RunRecorder((uint)width, (uint)height);
        var game = TwoMovesWithACapture();
        var display = new PixelGameDisplay<RgbaImage>(renderer);
        display.ResetGame(game);
        display.UI.Mode = GameUIMode.Playback;
        display.Render();
        return (renderer, display, game);
    }

    // The sizes below bracket the case that broke. The history panel is the flanking gutter clamped to
    // HistoryPanelWidth (18 em) — and on every surface aspect between the flanked/stacked crossover and
    // about 1.53 that gutter sits at its MinSideGutter floor of 11 em instead. The header's title (7.6 em)
    // and its chip (4.5 em) do not both fit that, nor do the longest move notations fit their Star cells.
    // 16:10 and portrait are the roomy cases, included so a "fix" that merely shrank everything can't pass.
    public static TheoryData<int, int> PanelWidths() => new()
    {
        { 1440, 1080 },  // 4:3   — gutter at its floor; header overlapped by 24 px before the fix
        { 1400, 1000 },  // 1.40  — 21 px
        { 1500, 1000 },  // 1.50  — 7 px, the edge of the band
        { 1600, 1100 },  // 1.45  — 16 px
        { 1600, 1000 },  // 16:10 — roomy gutter, always fitted
        { 1280, 800 },   // 16:10, the GUI's default window
        { 1080, 2408 },  // portrait — stacked, so the panel is the full surface width
    };

    /// <summary>
    /// The history header's title must never be laid over its mode chip. Asserted on the rects the engine
    /// arranged, which is the property the fix actually establishes: the chip is a docked strip of its own
    /// measured width and the title takes the remainder, so the two cannot intersect at any size.
    /// </summary>
    [Theory]
    [MemberData(nameof(PanelWidths))]
    public void History_header_title_never_overlaps_its_mode_chip(int width, int height)
    {
        var (renderer, display, game) = RenderPlayback(width, height);
        using var _ = renderer;

        var runs = display.GetCapturedLayout()
            .Where(n => n.Node is Layout.Node.Leaf { Content: Layout.Content.Text })
            .Select(n => (Text: ((Layout.Content.Text)((Layout.Node.Leaf)n.Node).Content).Value, n.Bounds))
            .ToList();

        var title = runs.SingleOrDefault(r => r.Text == "Move History");
        var chip = runs.SingleOrDefault(r => r.Text.Contains('▶'));
        title.Text.ShouldNotBeNull($"{width}x{height}: the header drew no title");
        chip.Text.ShouldNotBeNull($"{width}x{height}: playback drew no exit chip");

        (title.Bounds.X + title.Bounds.Width).ShouldBeLessThanOrEqualTo(chip.Bounds.X + 0.5f,
            $"{width}x{height}: the title's rect runs into the chip's — the header is hand-split again");

        // And the chip is still a control: draw == hit, so its own drawn rect must answer the tap that
        // leaves playback (the index one past the last ply is GameUI's exit sentinel).
        var hit = display.HitTest(chip.Bounds.X + chip.Bounds.Width / 2f, chip.Bounds.Y + chip.Bounds.Height / 2f);
        hit.ShouldBe(new HitResult.ListItemHit(GameUI.HistoryListId, game.Plies.Count),
            $"{width}x{height}: the chip's drawn rect does not answer a tap");
    }

    /// <summary>
    /// Nothing the display draws may be wider than the rect it was drawn into — the invariant that covers
    /// BOTH panel bugs at once, because a pixel surface neither clips nor wraps: a run wider than its rect
    /// simply draws over its neighbour, or off the screen entirely when the rect ends at the surface edge.
    ///
    /// <para>That is how the move rows were losing their tails: <c>Nc6xb4</c> overflowed its Star cell by
    /// ~15 px at 4:3, and since the panel's right edge IS the screen edge on a flanked frame, the missing
    /// part had nowhere to go. Both the header title and the ply cells now declare
    /// <see cref="TextTrim.Shrink"/> and the painter honours it, so this holds by construction rather than by
    /// every caller remembering to pre-measure.</para>
    ///
    /// <para>Measured at the size each run was ACTUALLY drawn at, which is the whole point — the authored
    /// size is what the tree asked for, not what reached the surface.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(PanelWidths))]
    public void No_text_run_is_drawn_wider_than_the_rect_it_was_given(int width, int height)
    {
        var (renderer, _, _) = RenderPlayback(width, height);
        using var _r = renderer;

        // Single characters are the board's own rank/file labels, drawn into deliberately generous label
        // margins rather than into a layout rect; the piece glyphs come from the Merida font. Everything
        // else is chrome sharing a strip with something, which is exactly what must fit.
        var chrome = renderer.Runs
            .Where(r => r.Font == FontPaths.DejaVuSans && r.Text.Trim().Length > 1)
            .ToList();

        chrome.ShouldNotBeEmpty($"{width}x{height}: recorded no chrome runs at all");

        foreach (var (text, font, fontSize, rect) in chrome)
        {
            var glyphs = renderer.MeasureText(text.AsSpan(), font, fontSize).Width;
            glyphs.ShouldBeLessThanOrEqualTo(rect.Width + 1f,
                $"{width}x{height}: \"{text}\" was drawn {glyphs:F0} px wide at {fontSize:F1} into a " +
                $"{rect.Width} px rect — it overhangs whatever sits beside it");
        }
    }

    // The touch heads' way back to the menu (iOS has no back key): a "‹ Menu" chip at the status bar's far end,
    // drawn where the status leaves room. Pins that it is there on the devices it was made for, in both
    // orientations, that a tap on it is recognised and a tap on the status text is not, and that it sits inside
    // the status bar. PNGs beside the test binary show it.
    [Theory]
    [InlineData(2360, 1640, 0, 40, "ipad-landscape")]  // iPad Air 11" (iPad15,3); 20 pt home indicator at 2x
    [InlineData(1640, 2360, 0, 40, "ipad-portrait")]
    [InlineData(1080, 2408, 100, 60, "phone-portrait")] // the Android phone with a punch-hole and a gesture bar
    public void Menu_chip_sits_at_the_status_bars_far_end(int width, int height, int top, int bottom, string label)
    {
        using var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        FillBackground(renderer.Surface.Pixels, PixelGameDisplay<RgbaImage>.Background);
        var display = new PixelGameDisplay<RgbaImage>(renderer)
        {
            SafeAreaInsets = (0, top, 0, bottom),
            KeyboardHints = false,
            MenuChip = true,
        };
        display.ResetGame(new Game());
        display.Render();
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, $"pixelgamedisplay-menuchip-{label}.png"),
            PngWriter.Encode(renderer.Surface.Pixels, renderer.Surface.Width, renderer.Surface.Height));

        var chip = display.MenuChipBounds.ShouldNotBeNull($"{label}: no room found for the chip");
        // Far end of the bar, above the bottom inset, and the bar's full height. Apple asks 44 pt (88 px at 2x) of a
        // touch target; the bar is two chrome fonts deep, which is 41 pt in iPad landscape, so the chip is a little
        // short there and makes it up in width (over 90 pt). 80 px pins that it never gets shallower than that.
        (chip.X + chip.Width).ShouldBe(width, 1f);
        (chip.Y + chip.Height).ShouldBe(height - bottom, 1f);
        chip.Height.ShouldBeGreaterThanOrEqualTo(80f);
        chip.Width.ShouldBeGreaterThanOrEqualTo(176f);

        display.IsMenuChipHit(chip.X + chip.Width / 2, chip.Y + chip.Height / 2).ShouldBeTrue();
        display.IsMenuChipHit(chip.X + 2, chip.Y + 2).ShouldBeTrue("the slop around the label is part of the chip");
        display.IsMenuChipHit(20, chip.Y + chip.Height / 2).ShouldBeFalse("the status text is not the chip");
        display.IsMenuChipHit(width / 2f, height / 2f).ShouldBeFalse("the board is not the chip");
    }

    [Fact]
    public void Menu_chip_never_squeezes_the_status()
    {
        // A status too long to share the bar keeps the bar, at its own size, and the chip waits for a shorter one.
        using var renderer = new RgbaImageRenderer(1080, 2408);
        var display = new PixelGameDisplay<RgbaImage>(renderer)
        {
            KeyboardHints = false,
            MenuChip = true,
            StatusOverride = "Waiting for your opponent to reconnect to the game over the network",
        };
        display.ResetGame(new Game());
        display.Render();
        display.MenuChipBounds.ShouldBeNull();
        display.IsMenuChipHit(1000, 2408 - 40).ShouldBeFalse();

        display.StatusOverride = null; // "White to move." fits beside it
        display.Render();
        display.MenuChipBounds.ShouldNotBeNull();
    }

    [Fact]
    public void Menu_chip_is_off_unless_the_host_asks_for_it()
    {
        // The desktop has Esc, and its status bar keeps the hints the chip would crowd.
        using var renderer = new RgbaImageRenderer(1600, 1000);
        var display = new PixelGameDisplay<RgbaImage>(renderer);
        display.ResetGame(new Game());
        display.Render();
        display.MenuChipBounds.ShouldBeNull();
        display.IsMenuChipHit(1550, 980).ShouldBeFalse();
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
