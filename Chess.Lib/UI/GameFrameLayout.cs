using System.Collections.Immutable;
using System.Numerics;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

namespace Chess.Lib.UI;

/// <summary>
/// How the chrome is arranged around the board. Not a preference — <see cref="GameFrameLayout"/> costs
/// every admissible shape in board squares and takes the winner, so this reports a decision rather than
/// recording one.
/// </summary>
public enum GameFrameShape
{
    /// <summary>
    /// Board beside a SINGLE history gutter, captured pieces riding in-board strips. Costs one gutter of
    /// width and no height, which makes it the cheapest shape on a wide, short surface — but it leaves the
    /// board off-centre, so only a host that never turns its frame may use it (see
    /// <see cref="GameFrameLayout.AllowOffCentreBoard"/>).
    /// </summary>
    SideBySide,

    /// <summary>
    /// Board centred between TWO gutters — history in one, the captured piles in the other. Costs twice the
    /// width of <see cref="SideBySide"/>, and buys back ~1.3 squares of height by moving the piles out of
    /// the board (<see cref="CapturedPiecesLayout.External"/>). Wins whenever height is the binding
    /// constraint, which on a landscape surface it usually is.
    /// </summary>
    Flanked,

    /// <summary>
    /// Full-width board with the history stacked BELOW it, piles in-board. Costs height rather than width,
    /// so it is the only shape that fits a surface too narrow for a gutter (phones in portrait, a tall
    /// narrow terminal).
    /// </summary>
    Stacked,
}

/// <summary>
/// The chrome's fixed sizes, in the frame tree's design units.
///
/// <para>The <b>shape</b> of the frame is shared across every front-end; these numbers are not, because
/// what reads as chrome differs by surface. A pixel host scales everything off a font size
/// (<see cref="FromChromeFontSize"/>); a terminal's status bar is one ROW and its history panel a
/// whole number of COLUMNS, and saying so in design units is both simpler and more accurate than
/// inheriting a font-derived pixel value that quantises to whichever cell count it lands nearest.</para>
///
/// <para>They feed the costing as well as the tree, so a surface is always costed with the chrome it
/// will actually draw.</para>
/// </summary>
/// <param name="StatusBarHeight">Depth of the status bar. <see cref="GameFrameShape.Flanked"/> reserves
/// this twice — once for the bar, once for its empty mirror above the board, which is what centres the
/// board vertically.</param>
/// <param name="HistoryPanelWidth">Width of the move-history panel.</param>
/// <param name="MinSideGutter">Narrowest a flanking gutter may be squeezed to. Its own Star min, so a
/// fixed-width panel can never starve the board to a negative width.</param>
/// <param name="MinStackedHistoryHeight">Least height worth stacking a history into; a
/// <see cref="GameFrameShape.Stacked"/> candidate that squeezes the history out isn't a fair win.</param>
public readonly record struct GameFrameMetrics(
    float StatusBarHeight,
    float HistoryPanelWidth,
    float MinSideGutter,
    float MinStackedHistoryHeight)
{
    /// <summary>
    /// A pixel surface's chrome, all of it proportional to the chrome font size — the convention
    /// <c>PixelGameDisplay</c> has always used.
    /// </summary>
    public static GameFrameMetrics FromChromeFontSize(float fontSize) => new(
        StatusBarHeight: fontSize * 2f,
        HistoryPanelWidth: fontSize * 18f,
        MinSideGutter: fontSize * 11f,
        MinStackedHistoryHeight: fontSize * 5f);

    /// <summary>
    /// A terminal's chrome, in design units of one pixel — a one-row status bar and a history panel
    /// <paramref name="historyColumns"/> cells wide. The gutter minimum IS the panel width: a terminal
    /// gutter holds nothing but the panel, so there is no narrower useful value.
    /// </summary>
    /// <param name="cellWidth">Pixel width of one character cell.</param>
    /// <param name="cellHeight">Pixel height of one character cell.</param>
    /// <param name="historyColumns">Columns the history panel occupies.</param>
    /// <param name="minStackedHistoryRows">Rows below which a stacked history isn't worth having
    /// (header + a few moves).</param>
    public static GameFrameMetrics FromCellSize(float cellWidth, float cellHeight,
        int historyColumns, int minStackedHistoryRows) => new(
        StatusBarHeight: cellHeight,
        HistoryPanelWidth: cellWidth * historyColumns,
        MinSideGutter: cellWidth * historyColumns,
        MinStackedHistoryHeight: cellHeight * minStackedHistoryRows);
}

/// <summary>
/// The chess frame, once, for every front-end: which shape the chrome takes, where each piece of it goes,
/// and — because the two answers have to agree — whether the captured piles ride in-board or in a gutter.
///
/// <para><b>Why this is shared and the painting is not.</b> Every front-end draws the same four regions
/// (board, history, captured piles, status bar) and faces the same trade: chrome placed beside the board
/// costs width, chrome placed below it costs height, and which is cheaper depends only on the shape of the
/// surface. That decision is arithmetic on the surface size, so it has no business being re-derived per
/// front-end — and when it was, the terminal sat on the wrong side of it and drew a board ~20% smaller
/// than its own window justified. What genuinely differs is the drawing: pixel hosts rasterise glyphs,
/// the terminal writes cells and blits Sixel. So this type resolves the frame and hands out rects;
/// callers paint them.</para>
///
/// <para><b>Surface-agnostic by construction.</b> <see cref="Layout.Node"/> carries sizes as design-unit
/// scalars and <see cref="Layout.Engine"/> is generic over the coordinate type, so ONE tree from
/// <see cref="Build"/> arranges to <c>float</c> pixels through <c>PixelMeasureContext</c> or to <c>int</c>
/// cells through Console.Lib's <c>CellMeasureContext</c> — the terminal passing its real cell size as the
/// design-unit convention, which makes a design unit exactly one Sixel pixel on both sides.</para>
/// </summary>
public sealed class GameFrameLayout
{
    // GameUI's natural aspect (height:width) WITH the captured strips — matches the web board canvas
    // (760x840 == 9.5:10.5). Used only by the stacked layout, which is the one that keeps the strips;
    // the other two hand the piles to a gutter or a single-gutter board and are shorter, so they must
    // not be sized through this constant.
    private const float BoardAspect = 10.5f / 9.5f;

    // Slot keys for the declarative frame: every Fill leaf routes its arranged rect to one painter, so
    // no chrome rect is ever computed by hand (Layout.Content.Fill.Key).
    public const string SlotBoard = "board";
    public const string SlotHistory = "history";
    public const string SlotCaptured = "captured";
    public const string SlotStatus = "status";

    private readonly GameFrameMetrics _metrics;
    private readonly bool _mirrorChrome;

    /// <param name="surfaceWidth">Full surface width in design units.</param>
    /// <param name="surfaceHeight">Full surface height in design units.</param>
    /// <param name="metrics">The chrome's fixed sizes — see <see cref="GameFrameMetrics"/>.</param>
    /// <param name="safeAreaInsets">Display cutouts / system bars to keep the frame clear of; zero on
    /// desktop, web and the terminal.</param>
    /// <param name="mirrorChrome">Docks the history on the opposite side (or above), so composing the
    /// frame with a 180° <c>ContentTransform</c> leaves every region physically put.</param>
    /// <param name="allowOffCentreBoard">See <see cref="AllowOffCentreBoard"/>.</param>
    public GameFrameLayout(
        float surfaceWidth,
        float surfaceHeight,
        GameFrameMetrics metrics,
        (int Left, int Top, int Right, int Bottom) safeAreaInsets = default,
        bool mirrorChrome = false,
        bool allowOffCentreBoard = false)
    {
        _metrics = metrics;
        _mirrorChrome = mirrorChrome;
        AllowOffCentreBoard = allowOffCentreBoard;

        // The frame lays out inside the safe area; the unsafe strips stay chrome-free (a top inset hosts
        // the host's own stats strip). Insets are zero on desktop, web and the terminal.
        var (l, t, r, b) = safeAreaInsets;
        SafeArea = new RectF32(l, t, surfaceWidth - l - r, surfaceHeight - t - b);

        Shape = ChooseShape();
    }

    /// <summary>
    /// Whether <see cref="GameFrameShape.SideBySide"/> is admissible — i.e. whether this host tolerates the
    /// board sitting off the surface centre.
    ///
    /// <para>False for every host that can turn its frame: the across-the-table 180° flip leaves a CENTRED
    /// board physically put and swings an off-centre one across the screen every move, so a centred board
    /// is a correctness requirement there, not a preference. That is the whole reason the flanked shape
    /// spends a second gutter it mostly leaves empty.</para>
    ///
    /// <para>True for the terminal, which never turns its frame and so may keep the width that second
    /// gutter would have cost.</para>
    /// </summary>
    public bool AllowOffCentreBoard { get; }

    /// <summary>The shape the costing chose. See <see cref="ChooseShape"/>.</summary>
    public GameFrameShape Shape { get; }

    /// <summary>The rect the frame is arranged into — the surface less any safe-area insets.</summary>
    public RectF32 SafeArea { get; }

    /// <summary>
    /// The piles live in a side gutter exactly when there is one to live in — which is only the flanked
    /// shape, the one that spends a whole gutter on them. Must agree with <see cref="Shape"/>, which is
    /// why both come from here rather than from each caller.
    /// </summary>
    public CapturedPiecesLayout CapturedLayout => Shape == GameFrameShape.Flanked
        ? CapturedPiecesLayout.External
        : CapturedPiecesLayout.Strips;

    /// <summary>True when the history docks beside the board rather than below it.</summary>
    public bool UseSideHistory => Shape != GameFrameShape.Stacked;

    /// <summary>
    /// Costs every admissible shape in board squares and takes the biggest board.
    ///
    /// <para>Each candidate is priced with the chrome it would actually draw — flanked pays two minimum
    /// gutters and a status band top AND bottom but drops the in-board strips, side-by-side pays one panel
    /// of width and one bar of height, stacked pays a bar plus a usable history strip — and
    /// <see cref="GameUI.CalculateSquareSize"/> turns each remaining area into the square size it would
    /// yield. Comparing squares rather than areas is what makes the answer meaningful: the board is
    /// whichever of width and height binds first, and that is exactly what a square size reports.</para>
    ///
    /// <para><b>Ties go to the shape that spends least.</b> Side-by-side wins a tie outright, then stacked
    /// over flanked — so a host with no admissible side-by-side reproduces the historical
    /// <c>flanked &gt; stacked</c> rule exactly, and a terminal on a surface where flanked draws level keeps
    /// the wider history panel it already had instead of trading it for nothing.</para>
    /// </summary>
    private GameFrameShape ChooseShape()
    {
        var totalW = (int)SafeArea.Width;
        var totalH = (int)SafeArea.Height;

        // Flanked: the minimum gutter each side, and a status-bar-height band top AND bottom (the bottom
        // one is the status bar, the top one its mirror — see FlankedFrame). The piles move out to a
        // gutter, which is worth ~1.3 squares of board height.
        var flanked = GameUI.CalculateSquareSize(
            (uint)Math.Max(0, totalW - 2 * (int)_metrics.MinSideGutter),
            (uint)Math.Max(0, totalH - 2 * (int)_metrics.StatusBarHeight),
            CapturedPiecesLayout.External);

        // Stacked: full width, one status bar, and enough left over for a usable history strip.
        var stacked = GameUI.CalculateSquareSize(
            (uint)totalW,
            (uint)Math.Max(0, totalH - (int)_metrics.StatusBarHeight - (int)_metrics.MinStackedHistoryHeight),
            CapturedPiecesLayout.Strips);

        // Side-by-side: one panel of width, one status bar of height, piles still in-board. No band above
        // the board, because nothing here has to stay centred.
        if (AllowOffCentreBoard)
        {
            var sideBySide = GameUI.CalculateSquareSize(
                (uint)Math.Max(0, totalW - (int)_metrics.HistoryPanelWidth),
                (uint)Math.Max(0, totalH - (int)_metrics.StatusBarHeight),
                CapturedPiecesLayout.Strips);

            if (sideBySide >= flanked && sideBySide >= stacked)
                return GameFrameShape.SideBySide;
        }

        return flanked > stacked ? GameFrameShape.Flanked : GameFrameShape.Stacked;
    }

    /// <summary>
    /// The frame as ONE declarative <see cref="Layout"/> tree. The status bar and its mirror band, the
    /// gutters that centre the board between them, the stacked history strip — all of it is Fixed/Star
    /// sizing resolved by DIR.Lib's engine, so there is no hand-rolled width/height/offset arithmetic left
    /// to drift. Star's MIN clamp is load-bearing: it is what stops a fixed-width panel from starving the
    /// board to a negative width on a narrow surface.
    ///
    /// <para><b>Two passes, for the flanked shape only.</b> No layout engine can resolve the board's
    /// <c>min(w / 9.5, h / 9.2)</c> aspect for us. With <paramref name="boardContentWidth"/> 0 (the sizing
    /// pass, on reset/resize) the board slot is the Star that takes everything the minimum gutters leave —
    /// the area <see cref="GameUI"/> sizes and centres itself into. With the width GameUI actually drew
    /// (the paint pass, per frame) the board slot becomes Fixed and the two EQUAL-weight gutter Stars split
    /// the real leftover; equal shares being exactly what keeps the board centred, and so invariant under
    /// the 180° flip. The other two shapes have no gutter to re-split and ignore the argument.</para>
    /// </summary>
    /// <remarks>
    /// <b>Every leaf states BOTH axes.</b> A <c>Fill</c> leaf has no intrinsic size, so a child that sets
    /// only its width keeps <c>Height</c> at <c>Auto</c>, measures a <c>MinHeight</c> of zero, and is
    /// arranged zero rows tall — the region silently vanishes. <c>ColW</c>/<c>RowH</c>/<c>Stretch</c> each
    /// set both axes at once, which is why they are used throughout in preference to spelling out
    /// <c>.WFixed().HStar()</c>.
    /// </remarks>
    public Layout.Node Build(float boardContentWidth = 0f) => Shape switch
    {
        GameFrameShape.Flanked => FlankedFrame(boardContentWidth),
        GameFrameShape.SideBySide => SideBySideFrame(),
        _ => StackedFrame(),
    };

    /// <summary>Board centred between two gutters, banded above and below by the status bar's height.</summary>
    private Layout.Node FlankedFrame(float boardContentWidth)
    {
        // Width known == the paint pass; see Build for why there are two.
        var paintPass = boardContentWidth > 0f;

        var board = paintPass
            ? Layout.Builder.Fill(key: SlotBoard).WFixed(boardContentWidth).HStar()
            : Layout.Builder.Fill(key: SlotBoard).WStar().HStar();

        Layout.Node Gutter(string key) => paintPass
            ? Layout.Builder.Fill(key: key).WStar(min: _metrics.MinSideGutter).HStar()
            : Layout.Builder.Fill(key: key).WFixed(_metrics.MinSideGutter).HStar();

        // History to the right of the board, captured piles to the left — swapped together by
        // MirrorChrome, so once the frame turns each is back on the physical side it started on.
        var (left, right) = _mirrorChrome ? (SlotHistory, SlotCaptured) : (SlotCaptured, SlotHistory);

        return Layout.Builder.VStack(
            // The status bar's empty mirror: reserving it top AND bottom is what centres the board
            // vertically on the safe area.
            Layout.Builder.Spacer().RowH(_metrics.StatusBarHeight),
            Layout.Builder.HStack(Gutter(left), board, Gutter(right)).Stretch(),
            Layout.Builder.Fill(key: SlotStatus).RowH(_metrics.StatusBarHeight));
    }

    /// <summary>
    /// Board beside one history panel, status bar under both. No mirror band above the board and no second
    /// gutter: this shape exists precisely because it declines to pay for centring, so paying for it here
    /// would defeat the point. There is no paint pass either — the panel is Fixed and the board Star takes
    /// what is left, so nothing needs the drawn width to resolve.
    /// </summary>
    private Layout.Node SideBySideFrame()
    {
        var board = Layout.Builder.Fill(key: SlotBoard).Stretch();
        var history = Layout.Builder.Fill(key: SlotHistory)
            // Clamped to the surface: on a terminal too narrow for the panel it takes what is there and
            // the board is left with nothing, rather than the frame overflowing.
            .ColW(MathF.Min(_metrics.HistoryPanelWidth, SafeArea.Width));

        var row = _mirrorChrome
            ? Layout.Builder.HStack(history, board)
            : Layout.Builder.HStack(board, history);

        return Layout.Builder.VStack(
            row.Stretch(),
            Layout.Builder.Fill(key: SlotStatus)
                .RowH(MathF.Min(_metrics.StatusBarHeight, SafeArea.Height)));
    }

    /// <summary>Full-width board with the history stacked in the strip left over above the status bar.</summary>
    private Layout.Node StackedFrame()
    {
        // The one number the engine can't resolve: the board's own aspect. Clamped so it never eats the
        // space below it, and the history Star then takes whatever remains.
        var availH = SafeArea.Height - _metrics.StatusBarHeight;
        var boardH = MathF.Min(availH, SafeArea.Width * BoardAspect);

        var board = Layout.Builder.Fill(key: SlotBoard).RowH(boardH);
        var history = Layout.Builder.Fill(key: SlotHistory).Stretch();
        var status = Layout.Builder.Fill(key: SlotStatus).RowH(_metrics.StatusBarHeight);

        // Mirrored: the history moves ABOVE the board, so the flip leaves both physically put.
        return _mirrorChrome
            ? Layout.Builder.VStack(history, board, status)
            : Layout.Builder.VStack(board, history, status);
    }

    /// <summary>
    /// The arranged rect of the <see cref="Layout.Content.Fill"/> leaf carrying <paramref name="key"/>;
    /// empty when this shape has no such slot (no captured gutter unless flanked). Generic over the
    /// coordinate type, so pixel and cell callers share the lookup as well as the tree.
    /// </summary>
    public static Rect<T> Slot<T>(ImmutableArray<Layout.ArrangedNode<T>> arranged, string key)
        where T : INumber<T>
    {
        foreach (var (node, rect) in arranged)
        {
            if (node is Layout.Node.Leaf { Content: Layout.Content.Fill fill } && fill.Key == key)
                return rect;
        }
        return default;
    }
}
