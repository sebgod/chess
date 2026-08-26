using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;
using Layout = DIR.Lib.Layout;

namespace Chess.Tests;

/// <summary>
/// The shared frame: which shape each surface gets, that the captured layout always agrees with it, and
/// that one tree arranges to both coordinate types without a region collapsing.
///
/// <para>These are the assertions that used to be impossible to write. The shape decision lived inside
/// <c>PixelGameDisplay.UseSideHistory</c> as a private property of a generic class over a live renderer, and
/// the terminal had a second, unrelated copy — so "does a wide terminal get the same shape as a wide
/// window" was not a question the test suite could ask.</para>
/// </summary>
public class GameFrameLayoutTests
{
    // Chess sizes pixel chrome from the surface height; see PixelGameDisplay.ChromeFontSize.
    private static float ChromeFontSize(float height) => MathF.Max(13f, (int)height / 40f);

    private static GameFrameLayout Pixels(float w, float h, bool mirror = false) =>
        new(w, h, GameFrameMetrics.FromChromeFontSize(ChromeFontSize(h)),
            mirrorChrome: mirror, allowOffCentreBoard: false);

    private static GameFrameLayout Terminal(int columns, int rows, int cellW = 10, int cellH = 20) =>
        new(columns * cellW, rows * cellH,
            GameFrameMetrics.FromCellSize(cellW, cellH, historyColumns: 24, minStackedHistoryRows: 5),
            allowOffCentreBoard: true);

    /// <summary>A dpiScale-1 pixel context: design units are surface units, as chess uses them.</summary>
    private sealed class UnitPixels : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
            => new(text.Length * fontSize * 0.5f, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }

    /// <summary>
    /// The frame tree must stay PAINTABLE-EMPTY: no Background, no Hit, and no leaf content beyond the
    /// slot Fills and the Spacer.
    /// </summary>
    /// <remarks>
    /// <para>This is not a style rule, it is the premise <c>PixelGameDisplay.ArrangeFrame</c> relies on.
    /// It publishes the frame to the layout capture buffer -- what the debug inspector's
    /// <c>describe_layout</c> reads -- by calling <c>PaintLayout</c> with a null <c>drawFill</c>, which
    /// draws nothing and registers nothing PRECISELY BECAUSE there is nothing here to draw. That call is
    /// a capture spelled as a paint.</para>
    /// <para>So the day someone tints a gutter with a <c>Background</c>, or hangs a <c>Hit</c> on a slot,
    /// that line stops being a capture and silently becomes a real paint -- chrome drawn twice, or a
    /// region registered across the board that eats the clicks GameUI hit-tests by geometry. Neither
    /// failure announces itself. This test is what turns that into a red build instead, and if it ever
    /// fails the answer is NOT to relax it: it is to route the Fills through a real <c>drawFill</c>, so
    /// that painting the frame is what the call actually means.</para>
    /// </remarks>
    [Theory]
    [InlineData(1920, 1080)]    // flanked
    [InlineData(1280, 800)]     // flanked, the window this is usually driven at
    [InlineData(1080, 2400)]    // stacked
    [InlineData(800, 1280)]     // stacked
    public void Frame_HasNothingToPaint_WhichIsWhatMakesCapturingItSafe(float w, float h)
    {
        var arranged = Layout.Engine.Arrange(
            Pixels(w, h).Build(), new Rect<float>(0, 0, w, h), new UnitPixels());

        foreach (var node in arranged)
        {
            var where = $"{w}x{h} {node.Node.GetType().Name} at ({node.Bounds.X}, {node.Bounds.Y})";

            node.Node.Background.ShouldBeNull($"{where}: a Background would be PAINTED by the capture");
            node.Node.Hit.ShouldBeNull($"{where}: a Hit would REGISTER a region over the board");
            node.Node.OnClick.ShouldBeNull($"{where}: an OnClick would register a region too");

            if (node.Node is not Layout.Node.Leaf leaf) continue;

            // Fill is the slot escape hatch (drawn by the painter that owns the key, not by the capture)
            // and Box is the Spacer. Text/Icon/TextInput would all put ink on the surface.
            leaf.Content.ShouldSatisfyAllConditions(
                () => (leaf.Content is Layout.Content.Fill or Layout.Content.Box).ShouldBeTrue(
                    $"{where}: leaf content {leaf.Content.GetType().Name} would be painted by the capture"));
        }
    }

    [Theory]
    // Desktop windows and tablets are wide enough for two gutters, and height binds, so moving the piles
    // out of the board is worth more than the width the second gutter costs.
    [InlineData(1920, 1080, GameFrameShape.Flanked)]
    [InlineData(1280, 720, GameFrameShape.Flanked)]
    [InlineData(1280, 800, GameFrameShape.Flanked)]
    // A tablet in portrait and a phone in either orientation cannot afford two gutters.
    [InlineData(1080, 2400, GameFrameShape.Stacked)]
    [InlineData(800, 1280, GameFrameShape.Stacked)]
    // A NEAR-SQUARE landscape surface still stacks, and this is the interesting case: chess sizes its
    // chrome off the surface HEIGHT, so a flanked gutter costs 0.275x the height whatever the width is.
    // Two of them eat 43% of a 1050-wide surface, which starves a flanked board below what a stacked one
    // yields — the crossover sits near 1.32:1. Stacked here is height-bound with width to spare, so it
    // is also the shape where a board that ignores its costing swallows the history whole.
    [InlineData(1050, 830, GameFrameShape.Stacked)]
    public void PixelSurface_GetsTheShapeItsProportionsAfford(float w, float h, GameFrameShape expected)
        => Pixels(w, h).Shape.ShouldBe(expected);

    /// <summary>
    /// A stacked candidate is COSTED with <see cref="GameFrameMetrics.MinStackedHistoryHeight"/> already
    /// deducted, so the shape owes that strip: a board that grows back into it wins a comparison it never
    /// paid for. And the failure is silent — a Star left with nothing resolves to zero rather than
    /// complaining — so the panel does not get cramped, it disappears.
    ///
    /// <para>Which is what shipped. Every surface wider than 1 : <c>BoardAspect</c> is height-bound in this
    /// shape, so on the near-square desktop window and the web canvas alike the history arranged zero pixels
    /// tall and all three pixel hosts drew a board with no move list beside or below it. The suite could not
    /// see it because the only stacked surfaces it probed were PORTRAIT, where the board is width-bound and
    /// leaves the strip alone of its own accord.</para>
    /// </summary>
    [Theory]
    [InlineData(1050, 830)]    // near-square: height-bound, the regression
    [InlineData(1380, 1085)]   // the web canvas at a real backing size
    [InlineData(1050, 1000)]   // barely landscape, and still height-bound
    [InlineData(1080, 2400)]   // portrait: width-bound, so the board leaves far more than the minimum
    public void StackedBoard_LeavesTheHistoryStripItsCostingReserved(float w, float h)
    {
        var frame = Pixels(w, h);
        frame.Shape.ShouldBe(GameFrameShape.Stacked);

        var arranged = Layout.Engine.Arrange(frame.Build(),
            new Rect<float>(0, 0, w, h), new UnitPixels());
        var history = GameFrameLayout.Slot(arranged, GameFrameLayout.SlotHistory);

        // Not merely non-zero: at least the depth the costing spent, or the win was fictitious. (This is
        // also PixelGameDisplay's own threshold for painting a stacked history at all.)
        history.Height.ShouldBeGreaterThanOrEqualTo(
            ChromeFontSize(h) * 5f, $"{w}x{h} stacked history depth");
        history.Width.ShouldBe(w, tolerance: 0.5f);
    }

    /// <summary>
    /// The rule that makes the flanked shape spend a gutter it mostly leaves empty: every pixel host can
    /// turn its frame 180° for across-the-table play, and only a CENTRED board stays physically put when it
    /// does. So side-by-side — which is cheaper on every wide surface — must never be offered to them, and
    /// the choice collapses to the historical <c>flanked &gt; stacked</c>.
    /// </summary>
    [Fact]
    public void PixelSurface_IsNeverOfferedTheOffCentreShape()
    {
        for (var w = 320; w <= 3840; w += 160)
        {
            for (var h = 240; h <= 2400; h += 120)
            {
                Pixels(w, h).Shape.ShouldNotBe(GameFrameShape.SideBySide, $"{w}x{h}");
            }
        }
    }

    [Theory]
    // A terminal is landscape in PIXELS — cells are about twice as tall as they are wide — so a roomy
    // window behaves like a desktop window and flanks. This is the case the console used to get wrong.
    [InlineData(108, 30, GameFrameShape.Flanked)]
    [InlineData(150, 40, GameFrameShape.Flanked)]
    // Small windows keep the cheap single gutter: two gutters would leave the board less than one.
    [InlineData(60, 12, GameFrameShape.SideBySide)]
    [InlineData(80, 24, GameFrameShape.SideBySide)]
    // A tall narrow terminal has no room beside the board at all, exactly like a phone in portrait.
    [InlineData(60, 50, GameFrameShape.Stacked)]
    public void Terminal_GetsTheShapeItsProportionsAfford(int columns, int rows, GameFrameShape expected)
        => Terminal(columns, rows).Shape.ShouldBe(expected);

    /// <summary>
    /// The console's historical frame — history in one gutter, piles in-board strips — is the side-by-side
    /// shape, so the terminal sizes that used to produce it still do. Joining the shared costing was only
    /// allowed to ADD shapes where they win, never to trade a smaller board for uniformity.
    /// </summary>
    [Theory]
    [InlineData(60, 12)]
    [InlineData(80, 24)]
    [InlineData(60, 20)]
    public void Terminal_KeepsItsOldShapeWhereItWasAlreadyTheBiggestBoard(int columns, int rows)
    {
        var frame = Terminal(columns, rows);

        frame.Shape.ShouldBe(GameFrameShape.SideBySide);
        frame.CapturedLayout.ShouldBe(CapturedPiecesLayout.Strips);
        frame.UseSideHistory.ShouldBeTrue();
    }

    /// <summary>
    /// The piles are drawn by GameUI (in-board strips) or by the host (a gutter tray) and never both, so a
    /// shape that disagrees with its captured layout means one of them is drawn nowhere. Both answers come
    /// from this type precisely so they cannot drift apart.
    /// </summary>
    [Fact]
    public void CapturedLayout_IsExternalExactlyWhenFlanked()
    {
        foreach (var frame in AllShapes())
        {
            frame.CapturedLayout.ShouldBe(
                frame.Shape == GameFrameShape.Flanked
                    ? CapturedPiecesLayout.External
                    : CapturedPiecesLayout.Strips,
                $"{frame.Shape}");
        }
    }

    /// <summary>
    /// A <c>Fill</c> leaf has no intrinsic size, so one that states a width and leaves its height at
    /// <c>Auto</c> measures a MinHeight of zero and is arranged zero rows tall — the region vanishes with no
    /// error anywhere. It has bitten this repo and its sibling independently, which is why every slot is
    /// asserted non-degenerate rather than merely present.
    /// </summary>
    [Fact]
    public void EverySlotTheShapeDeclares_ArrangesNonDegenerate_InPixels()
    {
        foreach (var frame in AllShapes())
        {
            var arranged = Layout.Engine.Arrange(frame.Build(),
                new Rect<float>(frame.SafeArea.X, frame.SafeArea.Y, frame.SafeArea.Width, frame.SafeArea.Height),
                new UnitPixels());

            foreach (var key in DeclaredSlots(frame.Shape))
            {
                var slot = GameFrameLayout.Slot(arranged, key);
                slot.Width.ShouldBeGreaterThan(0f, $"{frame.Shape}/{key} width");
                slot.Height.ShouldBeGreaterThan(0f, $"{frame.Shape}/{key} height");
            }
        }
    }

    /// <summary>
    /// The same tree, the same assertion, arranged to CELLS instead — which is the capability the shared
    /// frame rests on. Design units here are one Sixel pixel (the context is told the real cell size), so a
    /// region that survives in pixels can still round to zero rows on a coarse grid.
    /// </summary>
    [Theory]
    [InlineData(108, 30)]
    [InlineData(60, 12)]
    [InlineData(60, 50)]
    [InlineData(200, 50)]
    public void EverySlotTheShapeDeclares_ArrangesNonDegenerate_InCells(int columns, int rows)
    {
        const int cellW = 10, cellH = 20;
        var frame = Terminal(columns, rows, cellW, cellH);

        var arranged = Layout.Engine.Arrange(frame.Build(),
            new Rect<int>(0, 0, columns, rows), new CellMeasureContext(cellW, cellH));

        foreach (var key in DeclaredSlots(frame.Shape))
        {
            var slot = GameFrameLayout.Slot(arranged, key);
            slot.Width.ShouldBeGreaterThan(0, $"{frame.Shape}/{key} columns");
            slot.Height.ShouldBeGreaterThan(0, $"{frame.Shape}/{key} rows");
        }
    }

    /// <summary>
    /// The paint pass exists to make the gutters meet the board's real edge once its aspect has resolved.
    /// Equal shares are the point: an off-centre board swings across the screen under the across-the-table
    /// 180° flip, so this is what that flip's invariance rests on.
    /// </summary>
    [Fact]
    public void FlankedPaintPass_SplitsTheLeftoverEqually_SoTheBoardStaysCentred()
    {
        var frame = Pixels(1920, 1080);
        frame.Shape.ShouldBe(GameFrameShape.Flanked);

        // A board narrower than the sizing pass would have given it — the usual case, since the board's
        // height binds on a landscape surface and it declines the width it was offered.
        var arranged = Layout.Engine.Arrange(frame.Build(boardContentWidth: 900f),
            new Rect<float>(0, 0, 1920, 1080), new UnitPixels());

        var board = GameFrameLayout.Slot(arranged, GameFrameLayout.SlotBoard);
        var captured = GameFrameLayout.Slot(arranged, GameFrameLayout.SlotCaptured);
        var history = GameFrameLayout.Slot(arranged, GameFrameLayout.SlotHistory);

        board.Width.ShouldBe(900f);
        captured.Width.ShouldBe(history.Width, tolerance: 0.5f);
        (board.X - captured.X).ShouldBe(history.X + history.Width - (board.X + board.Width), tolerance: 0.5f);
    }

    /// <summary>
    /// Mirroring swaps the two gutters so that composing the frame with a 180° <c>ContentTransform</c>
    /// leaves each panel on the physical edge it started on — the across-the-table case where only the text
    /// orientation is meant to change and nothing visibly jumps sides.
    /// </summary>
    [Fact]
    public void MirrorChrome_SwapsTheGutters()
    {
        static (float Captured, float History) Gutters(bool mirror)
        {
            var frame = Pixels(1920, 1080, mirror);
            var arranged = Layout.Engine.Arrange(frame.Build(),
                new Rect<float>(0, 0, 1920, 1080), new UnitPixels());
            return (GameFrameLayout.Slot(arranged, GameFrameLayout.SlotCaptured).X,
                GameFrameLayout.Slot(arranged, GameFrameLayout.SlotHistory).X);
        }

        var (capturedLeft, historyRight) = Gutters(mirror: false);
        var (capturedRight, historyLeft) = Gutters(mirror: true);

        capturedLeft.ShouldBeLessThan(historyRight, "unmirrored: piles left, history right");
        historyLeft.ShouldBeLessThan(capturedRight, "mirrored: history left, piles right");
    }

    /// <summary>A spread of surfaces guaranteed to exercise all three shapes.</summary>
    private static IEnumerable<GameFrameLayout> AllShapes()
    {
        yield return Pixels(1920, 1080);   // Flanked
        yield return Pixels(1080, 2400);   // Stacked, width-bound (portrait)
        yield return Pixels(1050, 830);    // Stacked, HEIGHT-bound — the near-square landscape case
        yield return Terminal(108, 30);    // Flanked, in cells
        yield return Terminal(60, 12);     // SideBySide
        yield return Terminal(60, 50);     // Stacked, in cells
    }

    /// <summary>
    /// The slots a shape actually declares. Only the flanked shape has a captured gutter — the other two
    /// keep the piles in-board, so asserting a rect for one would be asserting on a region nothing draws.
    /// </summary>
    private static IEnumerable<string> DeclaredSlots(GameFrameShape shape)
    {
        yield return GameFrameLayout.SlotBoard;
        yield return GameFrameLayout.SlotHistory;
        yield return GameFrameLayout.SlotStatus;
        if (shape == GameFrameShape.Flanked) yield return GameFrameLayout.SlotCaptured;
    }
}
