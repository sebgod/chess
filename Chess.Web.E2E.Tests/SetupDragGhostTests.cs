using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// Browser E2E for the setup drag ghost (phase 4 of <c>docs/drag-ghost.md</c>): with a piece in hand,
/// the board redraws it under the pointer.
///
/// <para><b>These are the only tests here that read pixels, and that is deliberate rather than lazy.</b>
/// The rest of this suite asserts on the DOM surface because the board is a canvas and everything
/// worth checking has a DOM consequence. A ghost has none: the status line already says "moving White
/// Knight from b1" from the PRESS, before any motion, so no DOM assertion can tell a drawn ghost from
/// an undrawn one. Pixels are not a shortcut past the rule — they are the only thing that tests the
/// feature.</para>
///
/// <para>What is compared is whole PNGs rather than decoded pixels. It needs no decoder, and it is a
/// stronger claim than "something is different": the frame with the pointer over the board must differ
/// from the frame without it, AND moving off the board must return the canvas to byte-identically the
/// frame it started from. Noise cannot do that; only a ghost that appears and is cleanly removed can.</para>
///
/// <para>Playwright captures a WebGL canvas through the compositor, not the drawing buffer, so this
/// works even though <c>webgl-renderer.js</c> creates its context without <c>preserveDrawingBuffer</c>
/// — a <c>toDataURL</c>/<c>readPixels</c> approach would come back blank. That was verified before
/// these tests were written, and it is the assumption they rest on.</para>
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class SetupDragGhostTests(ChessWebFixture fixture)
{
    // WASM cold-boot (download runtime, load fonts, first frame) dwarfs any DOM settle time.
    private const float BootTimeout = 60_000;

    // Measured off the rendered setup board at a 1280x720 viewport: an 8x8 board of 60px squares with
    // its top-left corner at (399, 71) inside the #board canvas. Hard-coded rather than recomputed
    // from GameFrameLayout, which would be the layout logic written a second time and free to agree
    // with itself while both were wrong. The press below asserts on the status line instead, so a
    // board that moved fails loudly rather than quietly clicking bare squares.
    private const int BoardLeft = 399;
    private const int BoardTop = 71;
    private const int SquareSize = 60;

    private static ILocator Board(IPage page) => page.Locator("#board");
    private static ILocator Status(IPage page) => page.Locator(".status");

    /// <summary>Viewport coordinates of a square's centre. File 0 = a, rank 0 = 1.</summary>
    private static (float X, float Y) SquareCentre(LocatorBoundingBoxResult box, int file, int rank) => (
        box.X + BoardLeft + (file + 0.5f) * SquareSize,
        box.Y + BoardTop + (7 - rank + 0.5f) * SquareSize);

    // Walks the wizard into a custom game: Custom Game -> Standard Board -> White moves first ->
    // play as White -> Easy. Digits select-and-confirm, so each press is one whole step.
    private async Task<IPage> EnterSetupAsync()
    {
        var page = await fixture.NewPageAsync();
        // Pinned, because the square coordinates above were measured at this size.
        await page.SetViewportSizeAsync(1280, 720);
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Status(page)).ToContainTextAsync("Choose how you'd like to play",
            new() { Timeout = BootTimeout });

        await Board(page).FocusAsync();
        foreach (var key in new[] { "3", "2", "1", "1", "1" })
        {
            await page.Keyboard.PressAsync(key);
            await page.WaitForTimeoutAsync(150); // let Blazor's async keydown handler settle
        }

        await Expect(Status(page)).ToContainTextAsync("Set up the board", new() { Timeout = 15_000 });
        return page;
    }

    /// <summary>Presses the b1 knight and leaves it in hand. Releasing on the square the press started
    /// on is the tail of a click, which GameUI treats as a no-op — so the piece stays up.</summary>
    private async Task<LocatorBoundingBoxResult> PickUpTheKnightAsync(IPage page)
    {
        var box = (await Board(page).BoundingBoxAsync())!;
        var (x, y) = SquareCentre(box, file: 1, rank: 0);

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.UpAsync();

        // Doubles as the check that the measured geometry still lands on b1: an empty square would
        // open the piece palette and say something else entirely.
        await Expect(Status(page)).ToContainTextAsync("moving White Knight from b1", new() { Timeout = 10_000 });
        return box;
    }

    [Fact]
    public async Task PointerMove_WithAPieceInHand_RedrawsTheBoard()
    {
        var page = await EnterSetupAsync();
        var box = await PickUpTheKnightAsync(page);

        var inHand = await Board(page).ScreenshotAsync();

        var (dx, dy) = SquareCentre(box, file: 3, rank: 4); // d5, a long way from b1
        await page.Mouse.MoveAsync(dx, dy);
        await page.WaitForTimeoutAsync(250);

        var withGhost = await Board(page).ScreenshotAsync();

        Assert.False(withGhost.AsSpan().SequenceEqual(inHand),
            "moving the pointer with a piece in hand drew nothing — the canvas is byte-identical");
    }

    /// <summary>
    /// Off the board the ghost hides, and the canvas returns to exactly the frame it had before the
    /// pointer moved. Byte-identical is the point: it shows the ghost was removed CLEANLY rather than
    /// leaving the pixels it had been drawn over, which is the failure a repaint-the-region approach
    /// would produce and a whole-frame repaint would not.
    /// </summary>
    [Fact]
    public async Task PointerMove_OffTheBoard_HidesTheGhostAgain()
    {
        var page = await EnterSetupAsync();
        var box = await PickUpTheKnightAsync(page);

        var inHand = await Board(page).ScreenshotAsync();

        var (dx, dy) = SquareCentre(box, file: 3, rank: 4);
        await page.Mouse.MoveAsync(dx, dy);
        await page.WaitForTimeoutAsync(250);
        var withGhost = await Board(page).ScreenshotAsync();

        // The history panel, well right of the board.
        await page.Mouse.MoveAsync(box.X + box.Width - 40, box.Y + box.Height / 2);
        await page.WaitForTimeoutAsync(250);
        var offBoard = await Board(page).ScreenshotAsync();

        Assert.False(offBoard.AsSpan().SequenceEqual(withGhost), "the frame did not change when the pointer left the board");
        Assert.True(offBoard.AsSpan().SequenceEqual(inHand), "the board did not return to its pre-drag frame");

        // And the piece is still in hand — a release out there would be a no-op, and so is leaving.
        await Expect(Status(page)).ToContainTextAsync("moving White Knight from b1");
    }
}
