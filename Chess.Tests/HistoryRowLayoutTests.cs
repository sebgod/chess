using System.Collections.Immutable;
using System.Linq;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;
using Layout = DIR.Lib.Layout;

namespace Chess.Tests;

/// <summary>
/// The one move-history row, arranged on BOTH surfaces it serves: Console.Lib's cell grid (via
/// <see cref="CellMeasureContext"/>) and a pixel surface (via <see cref="PixelMeasureContext{TSurface}"/> over
/// the CPU <see cref="RgbaImageRenderer"/>). The row states no absolute extent, so these are the tests that
/// say why that works — and the pixel ones would catch a font whose figures are not tabular, which is the
/// assumption the shared row rests on.
/// </summary>
public sealed class HistoryRowLayoutTests
{
    private static readonly HistoryRowPalette Palette = new(
        Index: new RGBAColor32(0x80, 0x80, 0x98, 0xff),
        Ply: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        Highlight: new RGBAColor32(0xff, 0xd7, 0x00, 0xff),
        HighlightBackground: new RGBAColor32(0x30, 0x50, 0x90, 0xff));

    /// <summary>Twelve uneventful moves — enough that the last move number takes two digits, which is what
    /// the tabular-figures assumption is about. Same list as the console layout tests, for the same reason:
    /// no capture, check or repetition that would end the game early.</summary>
    private static ImmutableList<RecordedPly> TwelveMoves()
    {
        var game = new Game();
        (Position From, Position To)[] moves =
        [
            (Position.A2, Position.A4), (Position.A7, Position.A5),
            (Position.B2, Position.B4), (Position.B7, Position.B5),
            (Position.C2, Position.C4), (Position.C7, Position.C5),
            (Position.D2, Position.D4), (Position.D7, Position.D5),
            (Position.E2, Position.E4), (Position.E7, Position.E5),
            (Position.F2, Position.F4), (Position.F7, Position.F5),
            (Position.G2, Position.G4), (Position.G7, Position.G5),
            (Position.H2, Position.H4), (Position.H7, Position.H5),
            (Position.B1, Position.C3), (Position.B8, Position.C6),
            (Position.G1, Position.F3), (Position.G8, Position.F6),
            (Position.A1, Position.A3), (Position.A8, Position.A6),
            (Position.C1, Position.E3), (Position.C8, Position.E6),
        ];

        foreach (var (from, to) in moves)
        {
            game.TryMove(from, to).IsMoveOrCapture().ShouldBeTrue($"{from}{to} should apply");
        }

        return game.Plies;
    }

    /// <summary>The row's clickable cells in painting order: index, White's ply, Black's ply.</summary>
    private static (Layout.Node Node, Rect<T> Bounds, int PlyIndex)[] Cells<T>(
        ImmutableArray<Layout.ArrangedNode<T>> arranged) where T : System.Numerics.INumber<T>
        => [.. arranged
            .Where(a => a.Node.Hit is HitResult.ListItemHit)
            .Select(a => (a.Node, a.Bounds, ((HitResult.ListItemHit)a.Node.Hit!).Index))];

    private static ImmutableArray<Layout.ArrangedNode<int>> OnCells(
        ImmutableList<RecordedPly> plies, int moveIndex, int? highlight = null, int columns = 23)
        => Layout.Engine.Arrange(
            HistoryRowLayout.BuildRow(plies, moveIndex, highlight, 1f, Palette),
            new Rect<int>(0, 0, columns, 1),
            CellMeasureContext.CellAuthored);

    private static ImmutableArray<Layout.ArrangedNode<float>> OnPixels(
        RgbaImageRenderer renderer, ImmutableList<RecordedPly> plies, int moveIndex,
        int? highlight = null, float width = 234f, float fontSize = 13f)
        => Layout.Engine.Arrange(
            HistoryRowLayout.BuildRow(plies, moveIndex, highlight, fontSize, Palette),
            new Rect<float>(0f, 0f, width, fontSize * 1.5f),
            new PixelMeasureContext<RgbaImage>(renderer, FontPaths.DejaVuSans));

    /// <summary>
    /// The terminal's columns, which the console click theory pins from the other end
    /// (<c>ConsoleGameDisplayLayoutTests.HistoryClick_PicksThePlyPaintedUnderIt</c>): a 7-column index cell
    /// — <c>" {0,4}. "</c> — then the two plies splitting the remaining 16.
    /// </summary>
    [Fact]
    public void OnACellSurface_TheIndexIsItsTextAndThePliesSplitTheRest()
    {
        var cells = Cells(OnCells(TwelveMoves(), moveIndex: 0));

        cells.Length.ShouldBe(3);
        (cells[0].Bounds.X, cells[0].Bounds.Width).ShouldBe((0, 7), "the index cell is its own padded text");
        (cells[1].Bounds.X, cells[1].Bounds.Width).ShouldBe((7, 8), "White's ply takes half of what is left");
        (cells[2].Bounds.X, cells[2].Bounds.Width).ShouldBe((15, 8), "Black's reply takes the other half");
    }

    /// <summary>The move number labels the move, so clicking it picks White's ply — the reading a user
    /// expects, and the one the terminal's row happened to give before the row was shared.</summary>
    [Fact]
    public void TheIndexCellClaimsWhitesPly()
    {
        var cells = Cells(OnCells(TwelveMoves(), moveIndex: 5));

        cells[0].PlyIndex.ShouldBe(10, "move 6's index cell hits White's ply");
        cells[1].PlyIndex.ShouldBe(10);
        cells[2].PlyIndex.ShouldBe(11);
    }

    /// <summary>
    /// The pixel surface arranges the SAME tree: the index column comes out as wide as its text and the two
    /// plies split the rest, with no fixed column and no unit convention declared anywhere.
    /// </summary>
    [Fact]
    public void OnAPixelSurface_TheSameTreeArrangesToPixels()
    {
        using var renderer = new RgbaImageRenderer(300, 100);

        var cells = Cells(OnPixels(renderer, TwelveMoves(), moveIndex: 0));

        cells.Length.ShouldBe(3);
        cells[0].Bounds.X.ShouldBe(0f);
        cells[0].Bounds.Width.ShouldBeGreaterThan(0f, "the index cell measured its own text");
        cells[1].Bounds.X.ShouldBe(cells[0].Bounds.Width, 0.01f, "White's ply starts where the index ends");
        cells[1].Bounds.Width.ShouldBe(cells[2].Bounds.Width, 0.01f, "the two plies split the remainder");
        cells[2].Bounds.Right.ShouldBe(234f, 0.01f, "the plies consume the row");
    }

    /// <summary>
    /// <b>The assumption the shared row rests on.</b> The index is padded to a constant character count, so on
    /// a cell surface every index column is 7 wide no matter the move number. On a PROPORTIONAL surface that
    /// only holds if the PAD CHARACTER advances like a digit — which is why the pad is U+2007 FIGURE SPACE,
    /// the space a font defines to be digit-width, and not an ordinary space. In DejaVu Sans at a 13px chrome
    /// font U+2007 and a digit both advance 8.2710px, so the four-character field measures identically at
    /// every move number.
    ///
    /// <para>It was an ordinary space until 2026-08-10, justified by "a space advances all but exactly like a
    /// digit — 8.2393 against 8.2710px, 1298 vs 1303 units of a 2048 em". That measurement was of
    /// <c>'n'</c>: DIR.Lib had a bug where an ink-free glyph dropped its <c>hmtx</c> advance and whitespace
    /// borrowed <c>'n'</c>'s instead, and <c>'n'</c> happens to sit within a thirtieth of a pixel of a digit.
    /// A DejaVu space is really 4.1323px — HALF a digit — so once DIR.Lib 7.13 measured it correctly the field
    /// drifted 4.14px per pad character and this test failed. Tabular figures make digits equal to EACH
    /// OTHER; they never made a digit equal to a space.</para>
    ///
    /// <para>The quarter-pixel tolerance is therefore regime-spanning, not slack: against the pinned DIR.Lib
    /// (≤7.12) U+2007 still borrows <c>'n'</c>'s advance and the field drifts ~0.1px, while against 7.13+ it
    /// is exact. It can tighten to an exact comparison once the pin moves past 7.13. Either way it stays far
    /// below the 4px-per-character drift a non-digit-width pad produces, which is the failure being
    /// caught — without it the terminal would stay perfect while the pixel ply columns silently jittered row
    /// to row.</para>
    /// </summary>
    [Fact]
    public void OnAPixelSurface_TheIndexColumnIsTheSameWidthForOneDigitAndTwo()
    {
        using var renderer = new RgbaImageRenderer(300, 100);
        var plies = TwelveMoves();

        var move1 = Cells(OnPixels(renderer, plies, moveIndex: 0));
        var move12 = Cells(OnPixels(renderer, plies, moveIndex: 11));

        move12[0].Bounds.Width.ShouldBe(move1[0].Bounds.Width, 0.25f,
            "the two-digit index must measure as wide as the one-digit one — the pad is digit-width");
        move12[1].Bounds.X.ShouldBe(move1[1].Bounds.X, 0.25f, "so the ply columns cannot drift between rows");
        move12[1].Bounds.Width.ShouldBe(move1[1].Bounds.Width, 0.25f);
    }

    /// <summary>
    /// The premise of the test above, asserted directly on the font instead of through an arranged layout —
    /// because the premise is what silently broke, and a column-width failure is a long way from naming its
    /// cause. The pad character must advance like a digit; that is the whole reason it is U+2007 and not a
    /// space.
    ///
    /// <para>Asserted positively only. The obvious companion — that an ordinary space is NOT digit-width —
    /// cannot be asserted while the pin is a DIR.Lib that measures whitespace as <c>'n'</c> (there a space
    /// reports 8.2393px, a thirtieth of a pixel off a digit). Add it when the pin moves past 7.13; until then
    /// this direction is the half that holds in both regimes.</para>
    /// </summary>
    [Fact]
    public void ThePadCharacterAdvancesLikeADigit()
    {
        using var renderer = new RgbaImageRenderer(300, 100);
        float Width(string s) => renderer.MeasureText(s.AsSpan(), FontPaths.DejaVuSans, 13f).Width;

        Width("\u2007").ShouldBe(Width("0"), 0.25f,
            "U+2007 FIGURE SPACE is defined to advance like a digit, and the index pad is built on that");
        Width("0").ShouldBe(Width("1"), 0.25f, "and the figures themselves are tabular, digit to digit");
    }

    /// <summary>The highlight is per PLY, not per row — the reason each ply is its own cell.</summary>
    [Theory]
    [InlineData(4, true, false)]
    [InlineData(5, false, true)]
    public void ThePlaybackCursorHighlightsOneCellOfTheRow(int highlightPly, bool white, bool black)
    {
        var cells = Cells(OnCells(TwelveMoves(), moveIndex: 2, highlight: highlightPly));

        (cells[1].Node.Background == Palette.HighlightBackground).ShouldBe(white);
        (cells[2].Node.Background == Palette.HighlightBackground).ShouldBe(black);
        cells[0].Node.Background.ShouldBeNull("the index cell is never the playback cursor");
    }

    /// <summary>
    /// A move White has played but Black has not answered yet: the empty half is a spacer, so nothing there
    /// claims a hit on a ply that does not exist — and White's column keeps its width, because the spacer
    /// still takes its star share.
    /// </summary>
    [Fact]
    public void AMoveWithNoReplyYet_LeavesTheSecondHalfUnclickable()
    {
        var plies = TwelveMoves();
        var half = plies.RemoveAt(plies.Count - 1);   // 23 plies: move 12 has White only

        var cells = Cells(OnCells(half, moveIndex: 11));

        cells.Length.ShouldBe(2, "only the index cell and White's ply claim a click");
        cells.ShouldAllBe(c => c.PlyIndex == 22);
        (cells[1].Bounds.X, cells[1].Bounds.Width).ShouldBe((7, 8), "White's column is where it always is");
    }
}
