using System.IO;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using Console.Lib;
using DIR.Lib;
using SharpAstro.Png;
using Shouldly;
using Xunit;
using File = System.IO.File;

namespace Chess.Tests;

/// <summary>
/// Pins the flanked layout: on a surface wide enough for side gutters the board is CENTRED on the
/// surface and sized to the available height, with the history in one gutter and the captured piles
/// in the other. Centring is what makes the across-the-table 180° flip a no-op for the board — the
/// bug these guard is the board drifting toward the history panel every turn (measured on a Tab M8:
/// left edge 36px upright, 308px flipped) — and taking the piles out of the board area is what pays
/// for the bigger board. Renders over the CPU <see cref="RgbaImageRenderer"/>, no GPU/device needed;
/// set CHESS_LAYOUT_OUT to also dump each render as a PNG.
/// </summary>
public sealed class PixelGameDisplayCentringTests
{
    // Surfaces with room for gutters: 8" tablet, big tablet, phone landscape, desktop window.
    public static TheoryData<int, int, string> FlankedSurfaces => new()
    {
        { 1280, 800, "tab-landscape" },
        { 2000, 1200, "big-tablet" },
        { 2408, 1080, "phone-landscape" },
        { 1600, 1000, "desktop" },
    };

    [Theory]
    [MemberData(nameof(FlankedSurfaces))]
    public void Flanked_board_is_centred_on_the_surface(int width, int height, string label)
    {
        var r = Render(width, height, label: label);

        // ±1 for integer halving. A board centred on the surface maps onto ITSELF under the
        // across-the-table CenteredRotation(Half) — that is the whole point of centring it.
        (r.Content.UpperLeft.X + r.Content.LowerRight.X).ShouldBeInRange(width - 1, width + 1);
        (r.Content.UpperLeft.Y + r.Content.LowerRight.Y).ShouldBeInRange(height - 1, height + 1);
    }

    [Theory]
    [MemberData(nameof(FlankedSurfaces))]
    public void Mirroring_the_chrome_leaves_the_board_where_it_is(int width, int height, string label)
    {
        var normal = Render(width, height, label: label);
        var mirrored = Render(width, height, mirror: true, label: label + "-mirror");

        // MirrorChrome swaps the two gutters' CONTENTS; the board must not budge, or the composed
        // 180° flip would visibly shove it sideways every move.
        mirrored.Content.ShouldBe(normal.Content);
    }

    [Theory]
    [MemberData(nameof(FlankedSurfaces))]
    public void Flanked_board_fills_the_height_and_clears_the_status_bar(int width, int height, string label)
    {
        var r = Render(width, height, label: label);

        // Same formula as PixelGameDisplay.StatusBarHeight (ChromeFontSize × 2), which the board
        // must stay clear of at BOTH ends — the mirrored band up top is what keeps it centred.
        var statusBar = (int)(MathF.Max(13f, height / 40f) * 2f);
        r.Content.UpperLeft.Y.ShouldBeGreaterThanOrEqualTo(statusBar - 1,
            "board content overlaps the top band");
        r.Content.LowerRight.Y.ShouldBeLessThanOrEqualTo(height - statusBar + 1,
            "board content runs under the status bar (the file labels get clipped)");

        // Sized by the height it was given, not squeezed by the gutters: the whole point of
        // reserving only a MINIMUM gutter and letting the leftover widen it instead.
        var available = height - 2 * statusBar;
        r.Content.Height.ShouldBeGreaterThan((long)(available * 0.9),
            "the flanked board is not using the height available to it");
    }

    [Fact]
    public void Portrait_phone_still_stacks_the_history_below_a_full_width_board()
    {
        // Two gutters don't fit on a tall narrow screen, so the layout must fall back to the stacked
        // one — decided by surface shape, not by device kind. Stacked = the board hugs the top with
        // the history strip below it, so it is nowhere near vertically centred…
        var r = Render(1080, 2408, label: "phone-portrait");

        r.Content.UpperLeft.Y.ShouldBeLessThan(2408 / 4, "the portrait board should sit at the top");
        // …but it still spans (and is centred on) the full width, piles included.
        r.Content.Width.ShouldBeGreaterThan((long)(1080 * 0.9));
        (r.Content.UpperLeft.X + r.Content.LowerRight.X).ShouldBeInRange(1079, 1081);
    }

    [Fact]
    public void Captured_piles_sit_at_their_owner_s_end_of_the_board()
    {
        // The mid-game below leaves White two trophies (a queen and a pawn = two tray rows) and
        // Black one (two pawns = one row), so the piles are told apart by how much tray each drew.
        // White's belong at White's end of the board — which FLIPS with the board, so that under the
        // across-the-table 180° each player's trophies stay physically in front of them.
        var upright = Render(1280, 800, label: "captured-upright");
        var flipped = Render(1280, 800, flipBoard: true, label: "captured-flipped");

        upright.CapturedInBottomHalf.ShouldBeGreaterThan(upright.CapturedInTopHalf);
        flipped.CapturedInTopHalf.ShouldBeGreaterThan(flipped.CapturedInBottomHalf);

        // Same trophies either way — only their end of the gutter changed.
        (flipped.CapturedInTopHalf + flipped.CapturedInBottomHalf)
            .ShouldBe(upright.CapturedInTopHalf + upright.CapturedInBottomHalf);
    }

    // 1.e4 d5 2.exd5 Qxd5 3.Nc3 Qxa2 4.Rxa2 … — captures for both sides, of two different piece
    // types for White, so the piles are distinguishable by size.
    private static Game MidGame()
    {
        var game = new Game();
        foreach (var move in new[]
        {
            "e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5a2", "a1a2", "g8f6",
            "g1f3", "b8c6", "f1c4", "c8g4", "e1g1", "e7e6",
        })
        {
            game.TryMove(UciMove.Parse(move)).IsMoveOrCapture().ShouldBeTrue($"setup move {move} must be legal");
        }
        return game;
    }

    private sealed record Rendered(RectInt Content, int CapturedInTopHalf, int CapturedInBottomHalf);

    private static Rendered Render(int width, int height, bool mirror = false, bool flipBoard = false,
        string? label = null)
    {
        using var renderer = new RgbaImageRenderer((uint)width, (uint)height);

        // The host owns the base clear (see PixelGameDisplay.Background); without it the strips the
        // display doesn't paint stay transparent-black.
        var background = PixelGameDisplay<RgbaImage>.Background;
        var px = renderer.Surface.Pixels;
        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            px[i] = background.Red; px[i + 1] = background.Green;
            px[i + 2] = background.Blue; px[i + 3] = background.Alpha;
        }

        var display = new PixelGameDisplay<RgbaImage>(renderer) { MirrorChrome = mirror };
        display.ResetGame(MidGame());
        display.UI.FlipBoard = flipBoard;
        display.Render();

        if (label is not null && Environment.GetEnvironmentVariable("CHESS_LAYOUT_OUT") is { } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"layout-{label}.png"),
                PngWriter.Encode(px, renderer.Surface.Width, renderer.Surface.Height));
        }

        // GameUI tints the captured trays one step off the background (ComputeCapturedAreaColor:
        // ±20 per channel), a colour nothing else on the surface uses — so counting it per half of
        // the surface locates the two piles.
        var tray = (byte)(background.Red + 20);
        int top = 0, bottom = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (px[i] != tray || px[i + 1] != (byte)(background.Green + 20)) continue;
                if (y < height / 2) top++; else bottom++;
            }
        }

        return new Rendered(display.UI.ContentRect, top, bottom);
    }
}
