using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// What the board is PAINTED with, asserted on rendered pixels rather than on state.
///
/// <para>This file exists because of a bug no state assertion could have caught. The last-move border for a
/// CAPTURE was drawn in the same colour as a selected square's fill, so a captured-on square read as a piece
/// you had picked up. Every observable was correct — <c>Selected</c> null, <c>Mode</c> Playing, the right
/// response flags — and the defect lived entirely in which <c>RGBAColor32</c> got reused. The colours are
/// private, so the only way to assert a role is distinguishable is to render and look.</para>
/// </summary>
public class GameUIPaletteTests
{
    // Mirrors of GameUI's private palette. Duplicated deliberately: a test that read the real fields could
    // not fail when two roles were bound to the SAME field, which is exactly the bug.
    private static readonly RGBAColor32 SelectionRed = new(0xCD, 0x5C, 0x5C, 0xff);
    private static readonly RGBAColor32 CaptureViolet = new(0x8A, 0x4F, 0xD0, 0xff);
    private static readonly RGBAColor32 LastMoveGreen = new(0x48, 0xA0, 0x48, 0xff);

    private const uint Size = 720;

    /// <summary>Plays to a position whose last move is a capture: 1.e4 d5 2.exd5.</summary>
    private static Game AfterACapture()
    {
        var game = new Game();
        (Position From, Position To)[] moves =
        [
            (Position.E2, Position.E4), (Position.D7, Position.D5),
            (Position.E4, Position.D5),   // exd5 — a capture
        ];
        foreach (var (from, to) in moves)
        {
            game.TryMove(from, to).IsMoveOrCapture().ShouldBeTrue($"{from}{to}");
        }
        game.Plies[^1].Result.IsCapture().ShouldBeTrue("the last ply must be a capture");
        return game;
    }

    /// <summary>Plays to a position whose last move is NOT a capture: 1.Nc3.</summary>
    private static Game AfterAQuietMove()
    {
        var game = new Game();
        game.TryMove(Position.B1, Position.C3).IsMoveOrCapture().ShouldBeTrue();
        game.Plies[^1].Result.IsCapture().ShouldBeFalse();
        return game;
    }

    private static RgbaImage RenderOf(Game game, Position? selected = null)
    {
        var renderer = new RgbaImageRenderer(Size, Size);
        var ui = new GameUI(game, Size, Size, selected: selected);
        ui.Render<RgbaImage, Renderer<RgbaImage>>(renderer,
            new RectInt(new PointInt((int)Size, (int)Size), PointInt.Origin));
        return renderer.Surface;
    }

    private static int CountOf(RgbaImage img, RGBAColor32 colour)
    {
        var px = img.Pixels;
        var n = 0;
        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            if (px[i] == colour.Red && px[i + 1] == colour.Green && px[i + 2] == colour.Blue) n++;
        }
        return n;
    }

    /// <summary>
    /// The bug, directly. A capture's last-move border must not be painted in the colour that means
    /// "selected" — with nothing selected, that colour has no business being on the board at all.
    /// </summary>
    [Fact]
    public void ACapturesLastMoveBorder_IsNotTheSelectionColour()
    {
        var img = RenderOf(AfterACapture());

        CountOf(img, SelectionRed).ShouldBe(0,
            "nothing is selected, so the selection colour must appear nowhere");
        CountOf(img, CaptureViolet).ShouldBeGreaterThan(0,
            "the capture border is what should be drawn instead");
    }

    /// <summary>
    /// Guards the test above against being vacuous: if the selection colour were simply never painted, the
    /// zero-count assertion would hold for the wrong reason.
    /// </summary>
    [Fact]
    public void ASelectedSquare_IsPaintedInTheSelectionColour()
    {
        var img = RenderOf(AfterACapture(), selected: Position.D8);

        CountOf(img, SelectionRed).ShouldBeGreaterThan(0,
            "a selected square is filled with it, so the colour IS reachable");
    }

    /// <summary>
    /// The capture colour is a variant of the last-move marker, so it must differ from the quiet one too —
    /// otherwise the distinction it was introduced to carry is lost in the other direction.
    /// </summary>
    [Fact]
    public void AQuietLastMoveBorder_IsGreen_NotTheCaptureColour()
    {
        var img = RenderOf(AfterAQuietMove());

        CountOf(img, LastMoveGreen).ShouldBeGreaterThan(0);
        CountOf(img, CaptureViolet).ShouldBe(0, "no capture has happened");
    }

    /// <summary>
    /// The three roles must be mutually distinguishable, which is the invariant the original code broke by
    /// binding two of them to one field. Asserted on the constants a viewer actually sees.
    /// </summary>
    [Fact]
    public void SelectionCaptureAndQuietLastMove_AreThreeDistinctColours()
    {
        var all = new[] { SelectionRed, CaptureViolet, LastMoveGreen };

        all.Distinct().Count().ShouldBe(3);

        // Not merely unequal but far apart: the sixel wire format stores each channel as a 0..100
        // PERCENTAGE, so colours within ~2.5/255 of each other collapse to the same terminal palette entry.
        foreach (var (a, b) in all.SelectMany((a, i) => all.Skip(i + 1).Select(b => (a, b))))
        {
            var dr = a.Red - b.Red;
            var dg = a.Green - b.Green;
            var db = a.Blue - b.Blue;
            (dr * dr + dg * dg + db * db).ShouldBeGreaterThan(3 * 8 * 8,
                $"#{a.Red:X2}{a.Green:X2}{a.Blue:X2} and #{b.Red:X2}{b.Green:X2}{b.Blue:X2} are too close to survive sixel's percent quantisation");
        }
    }
}
