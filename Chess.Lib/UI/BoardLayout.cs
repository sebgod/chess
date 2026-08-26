using System.Collections.Immutable;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

namespace Chess.Lib.UI;

/// <summary>
/// What one leaf of the <see cref="BoardLayout"/> tree is. Rides on the leaf as a
/// <see cref="HitResult.SlotHit{T}"/>, which is what makes the drawn rect and the clickable rect the
/// same rect by construction rather than by two formulas agreeing.
/// </summary>
public enum BoardSlotKind : byte
{
    /// <summary>The 8x8 block as a whole — the squares' container, not a square.</summary>
    Grid,

    /// <summary>One playing square. <see cref="BoardSlot.Index"/> is <c>rowFromTop * 8 + col</c> in
    /// SCREEN cells, not a <see cref="Position"/> — see <see cref="BoardLayout"/> on the flip.</summary>
    Square,

    /// <summary>A file label above the board; index = column from the left.</summary>
    FileLabelTop,

    /// <summary>A file label below the board; index = column from the left.</summary>
    FileLabelBottom,

    /// <summary>A rank label left of the board; index = row from the top.</summary>
    RankLabelLeft,

    /// <summary>A rank label right of the board; index = row from the top.</summary>
    RankLabelRight,

    /// <summary>The captured-pieces band above the board (<see cref="CapturedPiecesLayout.Strips"/> only).</summary>
    CapturedTrayTop,

    /// <summary>The captured-pieces band below the board (<see cref="CapturedPiecesLayout.Strips"/> only).</summary>
    CapturedTrayBottom,
}

/// <summary>Which leaf of the board tree a point landed on. See <see cref="BoardSlotKind"/>.</summary>
public readonly record struct BoardSlot(BoardSlotKind Kind, int Index);

/// <summary>
/// The board itself as ONE declarative <see cref="Layout"/> tree: the two captured bands, the four
/// coordinate-label margins and the 8x8 grid of squares, all resolved by DIR.Lib's engine. What
/// <see cref="GameFrameLayout"/> did for the chrome AROUND the board, this does for what is inside it —
/// so no square, label or band rect is a hand-rolled <c>col * squareSize + margin + offset</c> any more.
///
/// <para><b>The flip is deliberately NOT in the tree.</b> Cells are SCREEN cells — column from the left,
/// row from the top — so <c>GameUI.DisplayCell</c>/<c>LogicalCell</c> stays the single place a
/// <see cref="Position"/> becomes a screen cell, exactly as before. Baking the orientation in would mean
/// rebuilding the tree whenever <c>GameUI.FlipBoard</c> is toggled, and would put a second copy of that
/// mapping somewhere it could disagree with the first.</para>
///
/// <para><b>Placement stays with the caller.</b> This tree owns what is INSIDE the content box; where
/// that box sits on the surface belongs to <see cref="GameUI"/> — centring slack, safe-area offsets, and
/// the cell-alignment quantization a Sixel host needs, none of which a Star weight can express. Same
/// division of labour as the frame: <see cref="GameFrameLayout"/> hands GameUI a board slot, GameUI
/// hands this an origin.</para>
///
/// <para><b>Arranged in <c>int</c>.</b> Every rect the board draws is a <see cref="RectInt"/> and the
/// engine is generic over its coordinate type, so arranging in int is exact — no float rect to round
/// back down, and no rounding to disagree with the hit test about which pixel is whose.</para>
/// </summary>
/// <remarks>
/// <b>Every leaf states BOTH axes</b> (<c>ColW</c>/<c>RowH</c>/<c>Stretch</c>). A <c>Fill</c> leaf has no
/// intrinsic size, so one that sets a single axis leaves the other <c>Auto</c>, measures zero, and is
/// arranged zero-extent — the region silently vanishes. Same rule, same reason, as the frame tree.
/// </remarks>
public sealed class BoardLayout
{
    /// <summary>Columns on the board. A literal today; the one number a non-chess grid would parameterise.</summary>
    public const int Files = 8;

    /// <inheritdoc cref="Files"/>
    public const int Ranks = 8;

    private readonly ImmutableArray<Layout.ArrangedNode<int>> _arranged;
    private readonly RectInt[] _squares = new RectInt[Files * Ranks];
    private readonly RectInt[] _fileLabels = new RectInt[2 * Files];
    private readonly RectInt[] _rankLabels = new RectInt[2 * Ranks];
    private readonly RectInt[] _trays = new RectInt[2];

    /// <param name="squareSize">Side of one playing square, in surface pixels.</param>
    /// <param name="labelMargin">The coordinate-label margin on all four sides.</param>
    /// <param name="capturedStripHeight">Height of each captured band, or 0 when the piles live outside
    /// the board area (<see cref="CapturedPiecesLayout.External"/>) — then no band is built at all.</param>
    /// <param name="origin">Upper-left of the content box, in surface coordinates.</param>
    public BoardLayout(int squareSize, int labelMargin, int capturedStripHeight, PointInt origin)
    {
        SquareSize = squareSize;
        LabelMargin = labelMargin;
        CapturedStripHeight = capturedStripHeight;

        Tree = Build();

        var width = Files * squareSize + 2 * labelMargin;
        var height = Ranks * squareSize + 2 * labelMargin + 2 * capturedStripHeight;
        Content = new RectInt((origin.X + width, origin.Y + height), (origin.X, origin.Y));

        _arranged = Layout.Engine.Arrange(
            Tree, new Rect<int>(origin.X, origin.Y, width, height), SurfacePixels.Instance);

        foreach (var (node, rect) in _arranged)
        {
            if (node.Hit is not HitResult.SlotHit<BoardSlot> hit)
            {
                continue;
            }

            var (kind, index) = hit.Slot;
            var r = ToRectInt(rect);
            switch (kind)
            {
                case BoardSlotKind.Grid: Board = r; break;
                case BoardSlotKind.Square: _squares[index] = r; break;
                case BoardSlotKind.FileLabelTop: _fileLabels[index] = r; break;
                case BoardSlotKind.FileLabelBottom: _fileLabels[Files + index] = r; break;
                case BoardSlotKind.RankLabelLeft: _rankLabels[index] = r; break;
                case BoardSlotKind.RankLabelRight: _rankLabels[Ranks + index] = r; break;
                case BoardSlotKind.CapturedTrayTop: _trays[0] = r; break;
                case BoardSlotKind.CapturedTrayBottom: _trays[1] = r; break;
            }
        }
    }

    /// <summary>The tree itself, for tests and for a host that wants to paint or describe it.</summary>
    public Layout.Node Tree { get; }

    /// <summary>Side of one playing square.</summary>
    public int SquareSize { get; }

    /// <summary>The coordinate-label margin on all four sides.</summary>
    public int LabelMargin { get; }

    /// <summary>Height of one captured band; 0 when there are none.</summary>
    public int CapturedStripHeight { get; }

    /// <summary>The whole content box: bands, label margins and squares.</summary>
    public RectInt Content { get; }

    /// <summary>The 8x8 block alone — labels and bands excluded.</summary>
    public RectInt Board { get; }

    /// <summary>The square at screen cell (<paramref name="col"/> from the left,
    /// <paramref name="rowFromTop"/> from the top).</summary>
    public RectInt Square(int col, int rowFromTop) => _squares[rowFromTop * Files + col];

    /// <summary>The file-label cell over (or under) column <paramref name="col"/>.</summary>
    public RectInt FileLabel(int col, bool bottom) => _fileLabels[(bottom ? Files : 0) + col];

    /// <summary>The rank-label cell left (or right) of row <paramref name="rowFromTop"/>.</summary>
    public RectInt RankLabel(int rowFromTop, bool right) => _rankLabels[(right ? Ranks : 0) + rowFromTop];

    /// <summary>The captured band above (or below) the board; empty when there are no bands.</summary>
    public RectInt CapturedTray(bool bottom) => _trays[bottom ? 1 : 0];

    /// <summary>
    /// Which slot a surface point lands on, or null for a point outside the content box. Walks the
    /// arranged nodes in reverse — last arranged is topmost, DIR.Lib's own hit-test convention — so a
    /// point on the seam between two squares resolves to the one drawn last, which is the one you can
    /// actually see there.
    /// </summary>
    public BoardSlot? HitTest(int x, int y)
    {
        for (var i = _arranged.Length - 1; i >= 0; i--)
        {
            var (node, rect) = _arranged[i];
            if (node.Hit is HitResult.SlotHit<BoardSlot> hit && ToRectInt(rect).Contains(x, y))
            {
                return hit.Slot;
            }
        }

        return null;
    }

    private Layout.Node Build()
    {
        // The label margins are a CROSS, not a ring: the four corners belong to nobody, which is why each
        // rank column carries a margin-tall spacer at either end rather than eight cells over the full
        // height.
        var middle = Layout.Builder.HStack(
            RankLabelColumn(BoardSlotKind.RankLabelLeft),
            Layout.Builder.VStack(
                FileLabelRow(BoardSlotKind.FileLabelTop),
                SquareGrid(),
                FileLabelRow(BoardSlotKind.FileLabelBottom)).ColW(Files * SquareSize),
            RankLabelColumn(BoardSlotKind.RankLabelRight)).Stretch();

        // No bands at all with External, rather than two zero-height ones: an empty leaf is still a leaf
        // every hit test and damage walk would have to keep discounting.
        return CapturedStripHeight > 0
            ? Layout.Builder.VStack(
                CapturedBand(BoardSlotKind.CapturedTrayTop),
                middle,
                CapturedBand(BoardSlotKind.CapturedTrayBottom))
            : Layout.Builder.VStack(middle);
    }

    private Layout.Node SquareGrid()
    {
        var cells = new Layout.Node[Files * Ranks];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = Slot(BoardSlotKind.Square, i);
        }

        // Star on both axes: the grid takes what the two label rows leave, which is exactly eight squares'
        // worth, and its own even split hands each cell exactly one square back.
        return Layout.Builder.Grid(Files, cells).Stretch()
            .Clickable(new HitResult.SlotHit<BoardSlot>(new BoardSlot(BoardSlotKind.Grid, 0)));
    }

    private Layout.Node FileLabelRow(BoardSlotKind kind)
    {
        var cells = new Layout.Node[Files];
        for (var col = 0; col < Files; col++)
        {
            cells[col] = Slot(kind, col);
        }

        return Layout.Builder.Grid(Files, cells).RowH(LabelMargin);
    }

    private Layout.Node RankLabelColumn(BoardSlotKind kind)
    {
        var cells = new Layout.Node[Ranks];
        for (var row = 0; row < Ranks; row++)
        {
            cells[row] = Slot(kind, row);
        }

        return Layout.Builder.VStack(
            Layout.Builder.Spacer().RowH(LabelMargin),
            Layout.Builder.Grid(1, cells).Stretch(),
            Layout.Builder.Spacer().RowH(LabelMargin)).ColW(LabelMargin);
    }

    /// <summary>
    /// A captured band, inset to the board's own columns rather than the full content width: the pile
    /// reads as a row belonging to the eight files above it, and the rank-label margins stay clear.
    /// </summary>
    private Layout.Node CapturedBand(BoardSlotKind kind) => Layout.Builder.HStack(
        Layout.Builder.Spacer().ColW(LabelMargin),
        Slot(kind, 0).ColW(Files * SquareSize),
        Layout.Builder.Spacer().ColW(LabelMargin)).RowH(CapturedStripHeight);

    private static Layout.Node Slot(BoardSlotKind kind, int index) => Layout.Builder.Fill().Stretch()
        .Clickable(new HitResult.SlotHit<BoardSlot>(new BoardSlot(kind, index)));

    private static RectInt ToRectInt(in Rect<int> rect) =>
        new((rect.X + rect.Width, rect.Y + rect.Height), (rect.X, rect.Y));

    /// <summary>
    /// Design units ARE surface pixels here: every scalar in this tree is a pixel count GameUI has
    /// already resolved (square size, label margin, band height), so the mapping is identity.
    /// <see cref="MeasureText"/> is never reached — the tree is Fill leaves and spacers, and the glyphs
    /// that go in them are drawn by GameUI into the rect the leaf was arranged at.
    /// </summary>
    private sealed class SurfacePixels : Layout.IMeasureContext<int>
    {
        public static readonly SurfacePixels Instance = new();

        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => Layout.Size<int>.Zero;

        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
    }
}
