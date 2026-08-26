using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Position;

namespace Chess.Tests;

/// <summary>
/// The board's own geometry, now that it is a declarative <c>Layout</c> tree rather than
/// <c>col * squareSize + margin + offset</c> repeated at every draw and hit-test site.
///
/// <para>Two halves. The first pins the tree itself against hand-computed rects — a square is exactly a
/// square, the label margins are a cross and not a ring, the captured bands span the eight board columns.
/// The second pins <see cref="GameUI"/> against the arithmetic it replaced, rebuilt here from nothing but
/// its public <c>ContentRect</c> and <c>SquareSize</c>, so a drift in the tree shows up as a red test
/// rather than as a board that renders one pixel off.</para>
/// </summary>
public class BoardLayoutTests
{
    private const int Square = 50;
    private const int Margin = 25;
    private const int Strip = 28;
    private const int OriginX = 10;
    private const int OriginY = 20;

    private static BoardLayout WithBands() => new(Square, Margin, Strip, new PointInt(OriginX, OriginY));

    private static BoardLayout NoBands() => new(Square, Margin, 0, new PointInt(OriginX, OriginY));

    private static RectInt Rect(int x, int y, int width, int height) =>
        new((x + width, y + height), (x, y));

    [Fact]
    public void The_content_box_is_the_bands_plus_the_label_cross_plus_the_squares()
    {
        WithBands().Content.ShouldBe(Rect(
            OriginX, OriginY,
            8 * Square + 2 * Margin,
            8 * Square + 2 * Margin + 2 * Strip));

        // Without the in-board bands the box is the label cross alone — the height the flanked frame
        // buys back for a bigger board.
        NoBands().Content.ShouldBe(Rect(OriginX, OriginY, 8 * Square + 2 * Margin, 8 * Square + 2 * Margin));
    }

    [Fact]
    public void The_board_block_sits_inside_the_label_cross_below_the_top_band()
    {
        WithBands().Board.ShouldBe(Rect(
            OriginX + Margin, OriginY + Strip + Margin, 8 * Square, 8 * Square));

        NoBands().Board.ShouldBe(Rect(OriginX + Margin, OriginY + Margin, 8 * Square, 8 * Square));
    }

    [Fact]
    public void Squares_tile_the_block_exactly()
    {
        var layout = WithBands();
        var board = layout.Board;

        for (var row = 0; row < 8; row++)
        {
            for (var col = 0; col < 8; col++)
            {
                // An even split of 8 squares' width over 8 columns must leave no remainder to
                // distribute — a one-pixel-wider column would put a seam through the piece glyphs.
                layout.Square(col, row).ShouldBe(
                    Rect(board.UpperLeft.X + col * Square, board.UpperLeft.Y + row * Square, Square, Square),
                    $"square at col {col}, row {row}");
            }
        }
    }

    [Fact]
    public void The_label_margins_are_a_cross_not_a_ring()
    {
        var layout = WithBands();
        var board = layout.Board;

        for (var idx = 0; idx < 8; idx++)
        {
            // File labels share their column with the squares under (over) them, and stop at the board.
            layout.FileLabel(idx, bottom: false).ShouldBe(
                Rect(board.UpperLeft.X + idx * Square, board.UpperLeft.Y - Margin, Square, Margin));
            layout.FileLabel(idx, bottom: true).ShouldBe(
                Rect(board.UpperLeft.X + idx * Square, board.LowerRight.Y, Square, Margin));

            // Rank labels share their ROW, which is what keeps the four corners unclaimed.
            layout.RankLabel(idx, right: false).ShouldBe(
                Rect(board.UpperLeft.X - Margin, board.UpperLeft.Y + idx * Square, Margin, Square));
            layout.RankLabel(idx, right: true).ShouldBe(
                Rect(board.LowerRight.X, board.UpperLeft.Y + idx * Square, Margin, Square));
        }
    }

    [Fact]
    public void Captured_bands_span_the_board_columns_and_hug_it()
    {
        var layout = WithBands();
        var board = layout.Board;

        layout.CapturedTray(bottom: false).ShouldBe(
            Rect(board.UpperLeft.X, board.UpperLeft.Y - Margin - Strip, 8 * Square, Strip));
        layout.CapturedTray(bottom: true).ShouldBe(
            Rect(board.UpperLeft.X, board.LowerRight.Y + Margin, 8 * Square, Strip));

        // External hands the piles to the host's gutter, so there is no band to report at all.
        NoBands().CapturedTray(bottom: false).ShouldBe(default(RectInt));
        NoBands().CapturedTray(bottom: true).ShouldBe(default(RectInt));
    }

    [Fact]
    public void Every_square_centre_hits_its_own_square()
    {
        var layout = WithBands();

        for (var row = 0; row < 8; row++)
        {
            for (var col = 0; col < 8; col++)
            {
                var rect = layout.Square(col, row);
                var hit = layout.HitTest(
                    rect.UpperLeft.X + Square / 2, rect.UpperLeft.Y + Square / 2);

                hit.ShouldBe(new BoardSlot(BoardSlotKind.Square, row * 8 + col), $"col {col}, row {row}");
            }
        }
    }

    [Fact]
    public void The_margins_answer_as_margins_and_the_outside_answers_as_nothing()
    {
        var layout = WithBands();
        var board = layout.Board;
        var midRow = board.UpperLeft.Y + Square / 2;

        layout.HitTest(board.UpperLeft.X - 1, midRow)
            .ShouldBe(new BoardSlot(BoardSlotKind.RankLabelLeft, 0));
        layout.HitTest(board.UpperLeft.X + Square / 2, board.UpperLeft.Y - 1)
            .ShouldBe(new BoardSlot(BoardSlotKind.FileLabelTop, 0));
        layout.HitTest(board.UpperLeft.X + Square / 2, layout.CapturedTray(bottom: false).UpperLeft.Y + 1)
            .ShouldBe(new BoardSlot(BoardSlotKind.CapturedTrayTop, 0));

        layout.HitTest(OriginX - 1, midRow).ShouldBeNull();
        layout.HitTest(OriginX, OriginY - 1).ShouldBeNull();
    }

    // ── GameUI: the arithmetic the tree replaced ───────────────────

    public static TheoryData<int, int> Surfaces => new()
    {
        { 800, 800 },
        { 1280, 800 },
        { 640, 900 },
        { 501, 733 }, // deliberately odd: centring slack and squareSize both have remainders
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Square_rects_match_the_arithmetic_they_replaced(int width, int height)
    {
        var ui = new GameUI(new Game(), (uint)width, (uint)height);

        // Rebuilt from the public surface alone: the content box is symmetric, so the label margin is
        // half of whatever it has beyond the eight squares, and the top band is one captured cell.
        var content = ui.ContentRect;
        var square = ui.SquareSize;
        var margin = ((int)content.Width - 8 * square) / 2;
        var strip = (int)MathF.Round(square * 0.4f * 1.4f);
        var boardLeft = content.UpperLeft.X + margin;
        var boardTop = content.UpperLeft.Y + strip + margin;

        for (byte file = 0; file < 8; file++)
        {
            for (byte rank = 0; rank < 8; rank++)
            {
                var position = Position.FromIndex(file, rank);
                ui.SquareRect(position).ShouldBe(
                    Rect(boardLeft + file * square, boardTop + (7 - rank) * square, square, square),
                    $"{position} at {width}x{height}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_selected_round_trips_every_square(bool flip)
    {
        var ui = new GameUI(new Game(), 800, 800) { FlipBoard = flip };

        for (byte file = 0; file < 8; file++)
        {
            for (byte rank = 0; rank < 8; rank++)
            {
                var position = Position.FromIndex(file, rank);
                var rect = ui.SquareRect(position);

                ui.FindSelected(
                    rect.UpperLeft.X + ui.SquareSize / 2,
                    rect.UpperLeft.Y + ui.SquareSize / 2).ShouldBe(position);
            }
        }
    }

    [Fact]
    public void A_click_on_the_rank_labels_is_not_a_click_on_the_a_file()
    {
        // The arithmetic this replaced divided by the square size, and C# truncates toward zero, so
        // every point up to one square LEFT of the board came back as column 0 — the rank labels down
        // the left edge silently selected the a-file, and the file labels above it selected rank 8.
        var ui = new GameUI(new Game(), 800, 800);
        var a8 = ui.SquareRect(A8);
        var midRow = a8.UpperLeft.Y + ui.SquareSize / 2;
        var midCol = a8.UpperLeft.X + ui.SquareSize / 2;

        ui.FindSelected(a8.UpperLeft.X - 2, midRow).ShouldBeNull();
        ui.FindSelected(midCol, a8.UpperLeft.Y - 2).ShouldBeNull();

        // ... while the square itself still answers, on both edges.
        ui.FindSelected(a8.UpperLeft.X + 2, midRow).ShouldBe(A8);
        ui.FindSelected(midCol, a8.UpperLeft.Y + 2).ShouldBe(A8);
    }

    [Fact]
    public void An_aligned_host_gets_squares_on_its_cell_boundaries()
    {
        // The Sixel display hands GameUI its terminal cell size, and every square boundary has to land
        // on one or the blit tears across cells. The tree preserves that only because the grid's even
        // split of eight squares over eight columns has NO remainder to distribute — a one-pixel-wider
        // column would push every square after it off the cell grid, silently.
        const uint cellW = 10;
        const uint cellH = 20;
        var ui = new GameUI(new Game(), 600, 560, alignment: (cellW, cellH));

        for (byte file = 0; file < 8; file++)
        {
            for (byte rank = 0; rank < 8; rank++)
            {
                var rect = ui.SquareRect(Position.FromIndex(file, rank));

                (rect.UpperLeft.X % (int)cellW).ShouldBe(0, $"x of {Position.FromIndex(file, rank)}");
                (rect.UpperLeft.Y % (int)cellH).ShouldBe(0, $"y of {Position.FromIndex(file, rank)}");
            }
        }
    }
}
