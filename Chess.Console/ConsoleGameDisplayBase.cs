using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;

#if DEBUG
using System.Diagnostics;
#endif

using File = Chess.Lib.File;
// DIR.Lib.Layout is a NAMESPACE, so the tree types need it aliased to read as Layout.Node /
// Layout.Builder -- the same alias GameFrameLayout uses for the same tree.
using Layout = DIR.Lib.Layout;

namespace Chess.Console;

/// <summary>
/// Base class for graphical game displays that render via a <see cref="Renderer{TSurface}"/>
/// and output Sixel to the terminal.
/// Handles layout, chrome (status bar + move history), GameUI management, and resize logic.
///
/// <para>The frame comes from <see cref="GameFrameLayout"/> — the SAME shared description the GUI, Android
/// and the browser arrange, chosen by costing each candidate shape in board squares. The terminal is not a
/// special case of it but a full participant: because <c>Layout.Node</c> carries design-unit scalars and
/// <c>Layout.Engine</c> is generic over the coordinate type, one tree arranges to <c>float</c> pixels there
/// and to <c>int</c> cells here through <see cref="CellMeasureContext"/>, which is told the terminal's real
/// cell size so that a design unit is exactly one Sixel pixel on both sides.</para>
///
/// <para><b>What the terminal gets out of joining in.</b> It is the one host allowed the
/// <see cref="GameFrameShape.SideBySide"/> shape — history in a single gutter, captured pieces in-board —
/// because it never turns its frame, and an off-centre board is only a problem for a host that does. But it
/// is also now allowed <see cref="GameFrameShape.Flanked"/>, which moves the piles into a gutter of their
/// own and so buys back the ~1.3 squares of height the in-board strips cost. A terminal is landscape in
/// pixel terms (cells are about twice as tall as they are wide), so height binds first and that trade is
/// almost always worth taking: a typical window gains 20-50% of board square. Which shape wins is
/// arithmetic, not a device rule, so a tall narrow terminal still stacks and a tiny one still keeps the
/// cheap single gutter.</para>
///
/// <para>Each widget is registered against a slot key, appears in the tree as a <c>Fill</c> leaf, and has
/// its viewport re-pointed at that leaf's arranged rect. The tree owns placement; the widgets own
/// behaviour — a <see cref="ScrollableList{T}"/> has scroll state and a thumb, a <see cref="Canvas"/> has
/// Sixel dirty regions, and neither is something a layout node can model.</para>
///
/// <para><b>Arranged on resize, not per frame</b>, which is where this deliberately parts company with
/// TianWen's TUI tabs. Every leaf here is a hosted widget — there is no node-painted chrome, no
/// background or label for <see cref="CellLayout.Paint"/> to draw — and the shape depends only on the
/// terminal size, never on game state. Re-arranging per frame would therefore paint nothing new while
/// repainting all three widgets, which is exactly what the clip-rect partial Sixel updates in
/// <see cref="RenderFrame"/> exist to avoid. (It also means the console runs only the frame's SIZING pass;
/// the paint pass exists to re-split gutters against the board's drawn width, and nothing here re-arranges
/// often enough to benefit.)</para>
/// </summary>
internal abstract class ConsoleGameDisplayBase<TSurface> : IGameDisplay
{
    /// <summary>
    /// Snapshot of rendering performance counters, split by pipeline stage so "is sixel the cost?"
    /// is answerable from the status bar: <paramref name="PaintMs"/> is the GameUI raster into the
    /// surface (plus the gutter tray), <paramref name="SixelMs"/> is <see cref="Canvas.Render()"/> —
    /// sixel encoding plus the buffered terminal write — and <paramref name="FlushMs"/> is shipping
    /// the bytes at <see cref="Present"/>. Flush lags one frame: the status bar is composed before
    /// its own frame ships, so the value shown is the PREVIOUS flush (which may also have been a
    /// text-only frame — widget updates flush too).
    /// </summary>
    internal readonly record struct RenderStats(
        double PaintMs, double SixelMs, double FlushMs, long FullRenders, long PartialRenders);

    private const int HistoryColumns = 24;

    /// <summary>Header plus a few moves — below this a stacked history isn't worth the height.</summary>
    private const int MinStackedHistoryRows = 5;

    private readonly IVirtualTerminal _terminal;
    private readonly Dictionary<string, HostedRegion> _hosts = [];
    private readonly HostedRegion _canvasHost;
    private readonly Canvas _boardCanvas;
    private readonly Renderer<TSurface> _renderer;
    private readonly TextBar _statusBar;
    private readonly ScrollableList<HistoryMoveRow> _historyList;

    private BoardPlacement _placement;
    private GameUI? _gameUI;

    /// <summary>The tray inputs the Sixel surface was last drawn against; see <see cref="TrayIsStale"/>.</summary>
    private (int Plies, GameUIMode Mode)? _trayState;

#if DEBUG
    private readonly Stopwatch _stopwatch = new();
    private double _lastPaintMs;
    private double _lastSixelMs;
    private double _lastFlushMs;
    private long _fullRenders;
    private long _partialRenders;
#endif

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    protected ConsoleGameDisplayBase(IVirtualTerminal terminal)
    {
        _terminal = terminal;

        _statusBar = new TextBar(Host(GameFrameLayout.SlotStatus));
        _historyList = new ScrollableList<HistoryMoveRow>(Host(GameFrameLayout.SlotHistory))
            .Header(" Move History");

        // The canvas is the one region NOT keyed to a single slot: when the frame gives the piles a gutter
        // they have to be drawn into the same renderer surface as the board (one surface, one Sixel blit),
        // so its viewport spans the union of the two. See ArrangeFrame.
        _canvasHost = new HostedRegion(new TerminalViewport(_terminal, 0, 0, 0, 0));

        // A hosted viewport's geometry is meaningless until the tree places it, and the renderer needs a
        // pixel size, so the first arrange has to happen before the board can be built.
        ArrangeFrame();

        var (width, height) = _canvasHost.Viewport.PixelSize;
        var (renderer, encoder) = CreateRenderer(width, height);
        _renderer = renderer;
        _boardCanvas = new Canvas(_canvasHost.Viewport, encoder);

#if CONSOLE_INSPECTOR
        InspectorHooks.Display = this;
#endif
    }

    protected abstract (Renderer<TSurface> Renderer, ISixelEncoder Encoder) CreateRenderer(uint width, uint height);

    internal RenderStats? Stats =>
#if DEBUG
        new(_lastPaintMs, _lastSixelMs, _lastFlushMs, _fullRenders, _partialRenders);
#else
        null;
#endif

    /// <summary>
    /// Where the board and the captured tray sit inside the Sixel canvas, in canvas-local pixels — the
    /// arrangement's output, and everything <see cref="GameUI"/> needs to be told about it. Offsets are
    /// canvas-local rather than terminal-absolute because GameUI draws into the renderer surface, which is
    /// the canvas; they are zero unless a captured gutter shifted the board right of the canvas origin.
    /// </summary>
    private readonly record struct BoardPlacement(
        CapturedPiecesLayout CapturedPieces,
        uint BoardWidth,
        uint BoardHeight,
        int BoardLeft,
        int BoardTop,
        int CapturedWidth)
    {
        /// <summary>True when the piles were given a gutter, so this display paints them itself.</summary>
        public bool HasCapturedGutter => CapturedPieces == CapturedPiecesLayout.External && CapturedWidth > 0;
    }

    /// <summary>
    /// Creates the viewport for the widget hosted at <paramref name="key"/>. Its geometry stays empty
    /// until <see cref="ArrangeFrame"/> places it.
    /// </summary>
    private ITerminalViewport Host(string key)
    {
        var region = new HostedRegion(new TerminalViewport(_terminal, 0, 0, 0, 0));
        _hosts[key] = region;
        return region.Viewport;
    }

    /// <summary>
    /// Re-arranges the frame from the shared <see cref="GameFrameLayout"/> and re-points every hosted
    /// viewport at its new rect. Returns whether any of them actually moved — the replacement for
    /// <c>Panel.Recompute()</c>'s "did the terminal change" guard, which is what keeps a per-pump
    /// <see cref="HandleResize"/> from repainting continuously.
    /// </summary>
    /// <remarks>
    /// The surface is described to the shared layout in PIXELS (columns x cell width) while the arrange
    /// runs in CELLS, which is the whole point of handing <see cref="CellMeasureContext"/> the real cell
    /// size: the costing then compares candidate shapes on the terminal's true pixel geometry — the thing
    /// that actually decides how big a board fits — and the tree's design units still resolve back to whole
    /// cells. A terminal has no safe-area insets, so the arrange bounds are simply the whole grid.
    /// </remarks>
    private bool ArrangeFrame()
    {
        var (columns, rows) = _terminal.Size;
        var cell = _terminal.CellSize;

        var frame = new GameFrameLayout(
            columns * cell.Width,
            rows * cell.Height,
            GameFrameMetrics.FromCellSize(cell.Width, cell.Height, HistoryColumns, MinStackedHistoryRows),
            // The terminal never turns its frame, so it may spend one gutter instead of two.
            allowOffCentreBoard: true);

        var arranged = Layout.Engine.Arrange(
            frame.Build(),
            new Rect<int>(0, 0, columns, rows),
            new CellMeasureContext(cell.Width, cell.Height));

        var board = GameFrameLayout.Slot(arranged, GameFrameLayout.SlotBoard);
        var captured = GameFrameLayout.Slot(arranged, GameFrameLayout.SlotCaptured);
        var hasCaptured = frame.CapturedLayout == CapturedPiecesLayout.External && captured.Width > 0;

        // One surface for the board and the piles, because one Canvas is one contiguous Sixel blit. The
        // two slots are adjacent by construction (the flanked tree is [captured | board | history], and
        // MirrorChrome — which would swap them — is a pixel-host concern the terminal never sets).
        var canvas = hasCaptured ? Union(captured, board) : board;

        var moved = _canvasHost.Place(canvas);
        foreach (var (node, rect) in arranged)
        {
            if (node is Layout.Node.Leaf { Content: Layout.Content.Fill { Key: { } key } }
                && _hosts.TryGetValue(key, out var host))
            {
                // Bitwise, not short-circuiting: every viewport must be re-pointed, not just the ones
                // before the first that moved.
                moved |= host.Place(rect);
            }
        }

        _placement = new BoardPlacement(
            frame.CapturedLayout,
            BoardWidth: (uint)(board.Width * cell.Width),
            BoardHeight: (uint)(board.Height * cell.Height),
            BoardLeft: (board.X - canvas.X) * cell.Width,
            BoardTop: (board.Y - canvas.Y) * cell.Height,
            CapturedWidth: hasCaptured ? captured.Width * cell.Width : 0);

        return moved;
    }

    private static Rect<int> Union(Rect<int> a, Rect<int> b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect<int>(x, y,
            Math.Max(a.X + a.Width, b.X + b.Width) - x,
            Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
    }

    private int? ResolveHistoryClick(int px, int py)
    {
        // Asked of the list, not reconstructed here: it owns the scroll state, so it is the only thing
        // that knows the header displaces row 0, that visible row N is item ScrollOffset + N, and that
        // the scrollbar owns the last column. Splitting the row by the full viewport width instead —
        // which is what this did before — turned a click on the scrollbar into a jump to Black's ply.
        if (_historyList.HitTestRow(px, py) is not (var moveIdx, _, var column, var columns))
            return null;

        var plyCount = UI.Game.PlyCount;
        var whitePlyIdx = moveIdx * 2;

        if (whitePlyIdx >= plyCount)
            return null;

        // A row reads "<n>. <white> <black>", so the right half of the CONTENT picks Black's ply.
        return column >= columns / 2 && whitePlyIdx + 1 < plyCount
            ? whitePlyIdx + 1
            : whitePlyIdx;
    }

    public void RenderInitial(Game game)
    {
        RenderFrame(UI, []);
        UpdateStatusBar(game);
        UpdateHistory(game);
        Present();
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh))
        {
            RenderFrame(UI, clipRects);
        }
        if (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement))
        {
            UpdateStatusBar(game);
            UpdateHistory(game);
        }
        Present();
    }

    /// <summary>
    /// Ends the frame. On a buffered terminal the widgets' writes sit in the back buffer until something
    /// flushes, and the only implicit flush is the next cursor move — so the last widget of a frame would
    /// otherwise appear one frame late. Immediate-mode terminals flush per write and this is free.
    /// </summary>
    private void Present()
    {
#if DEBUG
        _stopwatch.Restart();
        _terminal.Flush();
        _stopwatch.Stop();
        _lastFlushMs = _stopwatch.Elapsed.TotalMilliseconds;
#else
        _terminal.Flush();
#endif
    }

    private int? HighlightPlyIndex => UI.Mode == GameUIMode.Playback ? UI.PlaybackPlyIndex : null;

    private void UpdateStatusBar(Game game)
    {
        // Canonical mode-aware text from GameUI; the leading space is terminal-cell padding.
        var status = $" {UI.StatusLine()}";

        var debugInfo = "";
        if (Stats is { } s)
        {
            var total = s.FullRenders + s.PartialRenders;
            if (total > 0)
            {
                // Stage split, not one aggregate: paint (GameUI raster) / sixel (encode + buffered
                // write) / flush (shipping the bytes; lags one frame — see RenderStats).
                debugInfo = $"paint {s.PaintMs,5:F1} sixel {s.SixelMs,5:F1} flush {s.FlushMs,5:F1}ms  F:{s.FullRenders} P:{s.PartialRenders} ({100.0 * s.PartialRenders / total:F0}% partial) ";
            }
        }

        _statusBar.Text(status).RightText(debugInfo).Render();
    }

    private void UpdateHistory(Game game)
    {
        var plies = game.Plies;
        var (moveCount, _, startMove) = UI.HistoryWindow(_historyList.VisibleRows);
        var highlightPly = HighlightPlyIndex;

        var rows = new HistoryMoveRow[moveCount];
        for (var i = 0; i < moveCount; i++)
            rows[i] = new HistoryMoveRow(plies, i, highlightPly);

        _historyList
            .Items(rows)
            .ScrollTo(startMove)
            .Render();
    }

    public void HandleResize(Game game)
    {
#if CONSOLE_INSPECTOR
        // GameLoop calls HandleResize every pump, which makes it the one per-iteration hook the loop hands a
        // display -- and it runs on the loop thread, the same one that mutates GameUI. That is exactly what
        // the inspector's commands need, so they run here rather than on the socket thread.
        InspectorHooks.Pump();
#endif

        if (!ArrangeFrame())
            return;

        // Sixel pixels are NOT erased by drawing a smaller image over them, and a cell repaint does not
        // touch them either: shrinking the terminal leaves the whole previous, larger frame on screen
        // around the new one -- old board, old labels, old captured strips. Everything below repaints all
        // three widgets from scratch, so blanking first is both correct and no more work. It happens once
        // per real geometry change, not once per pump, because of the guard above.
        _terminal.Clear();

        var (width, height) = _boardCanvas.PixelSize;

        // A terminal too small to leave the board any cells has nothing to render into, and resizing a
        // renderer to an empty surface is not a thing to ask for. The next arrange that gives it room
        // reports moved again, so this recovers on its own.
        if (width == 0 || height == 0)
            return;

        _renderer.Resize(width, height);

        // A resize can change the SHAPE, not just the size — a terminal dragged from wide to tall crosses
        // from a flanked frame to a stacked one — so the captured layout has to be re-stated, or GameUI
        // would keep drawing in-board strips into a frame that has a gutter waiting for them (or worse,
        // stop drawing them with nowhere else for them to appear).
        _gameUI = UI.Resize(_placement.BoardWidth, _placement.BoardHeight,
            topOffset: _placement.BoardTop,
            leftOffset: _placement.BoardLeft,
            capturedLayout: _placement.CapturedPieces);

        UI.HistoryViewportRows = _historyList.VisibleRows;

        RenderFrame(UI, []);
        UpdateStatusBar(game);
        UpdateHistory(game);
        Present();
    }

    public void ResetGame(Game game)
    {
        var cell = _boardCanvas.Viewport.CellSize;
        _gameUI = new GameUI(game, _placement.BoardWidth, _placement.BoardHeight,
            mainFontColor: GameUI.PlainFontColor,
            backgroundColor: GameUI.PlainBackgroundColor,
            alignment: (cell.Width, cell.Height),
            resolveHistoryClick: ResolveHistoryClick,
            topOffset: _placement.BoardTop,
            leftOffset: _placement.BoardLeft,
            capturedLayout: _placement.CapturedPieces);

        // HandleResize is the other writer, but it only runs its body when the arrangement CHANGED, so a
        // game that is never resized would leave this at 0 -- and every scroll path divides the history
        // by it (ScrollHistory, PageUp/PageDown, EnsurePlyVisible). PixelGameDisplay sets it from its own
        // arrange for the same reason.
        _gameUI.HistoryViewportRows = _historyList.VisibleRows;
    }

    /// <summary>
    /// Whether the captured tray's content has moved on since the Sixel surface was last drawn.
    ///
    /// <para>Only the external tray needs asking: the in-board strips sit inside the area
    /// <see cref="GameUI.Render{TSurface, TRenderer}"/> already clips against, whereas a gutter tray is
    /// outside it and this display owns keeping it current. The tray is a pure function of the plies walked
    /// so far (GameUI counts captures by replaying them), so it goes stale on exactly the frames where that
    /// count moves — every ply, and every playback step — and stays valid across the ones that repeat
    /// most, selection and hover highlights.</para>
    ///
    /// <para>A stale tray forces a FULL blit rather than a wider clip because
    /// <see cref="Canvas.Render(RectInt)"/> crops vertically only: the tray spans the board's whole height,
    /// so unioning it into the clip would produce a full-height band regardless, and saying so plainly is
    /// cheaper to read than arriving at it by accident.</para>
    /// </summary>
    private bool TrayIsStale(GameUI ui)
    {
        if (!_placement.HasCapturedGutter) return false;

        var state = (ui.Mode == GameUIMode.Playback ? ui.PlaybackPlyIndex + 1 : ui.Game.PlyCount, ui.Mode);
        if (_trayState == state) return false;

        _trayState = state;
        return true;
    }

    private void RenderFrame(GameUI ui, ImmutableArray<RectInt> clipRects)
    {
        var renderer = _renderer;
        var trayIsStale = TrayIsStale(ui);

        RectInt clip;
        bool isPartial;
        if (!clipRects.IsDefault && clipRects.Length > 0 && !trayIsStale)
        {
            isPartial = true;
            clip = clipRects[0];
            for (var i = 1; i < clipRects.Length; i++)
            {
                clip = clip.Union(clipRects[i]);
            }
        }
        else
        {
            isPartial = false;
            clip = new RectInt((renderer.Width, renderer.Height), PointInt.Origin);
        }

#if DEBUG
        _stopwatch.Restart();
#endif

        ui.Render<TSurface, Renderer<TSurface>>(renderer, clip);

        if (_placement.HasCapturedGutter)
        {
            RenderCapturedTray(ui);
        }

#if DEBUG
        _stopwatch.Stop();
        _lastPaintMs = _stopwatch.Elapsed.TotalMilliseconds;
        _stopwatch.Restart();
#endif

        if (isPartial)
            _boardCanvas.Render(clip);
        else
            _boardCanvas.Render();

#if DEBUG
        _stopwatch.Stop();
        _lastSixelMs = _stopwatch.Elapsed.TotalMilliseconds;
        if (isPartial) _partialRenders++; else _fullRenders++;
#endif
    }

    /// <summary>
    /// Paints the captured piles into their gutter, flush against the board's drawn edge and spanning its
    /// height, so the two trays sit level with the ranks whose pieces they hold.
    ///
    /// <para>Measured off <see cref="GameUI.ContentRect"/> rather than off the gutter slot: the board
    /// absorbs whatever centring slack its slot had, so only the drawn box knows where its edge ended up.
    /// The gutter's own width is still the cap — the tray may not spill out of the space the frame gave
    /// it.</para>
    ///
    /// <para>The background fill is not decoration. A tray row only paints where a piece has actually been
    /// captured, and this surface is never cleared between frames (no <c>BeginFrame</c> here — that is what
    /// the pixel hosts have), so rewinding playback past a capture would otherwise leave the taller pile
    /// standing.</para>
    /// </summary>
    private void RenderCapturedTray(GameUI ui)
    {
        var content = ui.ContentRect;
        var width = Math.Min(_placement.CapturedWidth, ui.CapturedColumnWidth);
        var right = content.UpperLeft.X;
        var left = Math.Max(0, right - width);
        if (right <= left) return;

        var tray = new RectInt((right, (int)content.LowerRight.Y), (left, content.UpperLeft.Y));
        _renderer.FillRectangle(tray, GameUI.PlainBackgroundColor);
        ui.RenderCapturedColumn<TSurface, Renderer<TSurface>>(_renderer, tray);
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }

    /// <summary>
    /// One hosted widget's viewport, plus the rect it was last placed at, so a re-arrange can report
    /// whether anything really moved instead of the caller repainting on every pump.
    /// </summary>
    private sealed class HostedRegion(TerminalViewport viewport)
    {
        private Rect<int>? _last;

        /// <summary>
        /// The hosted viewport. Typed as the interface because placing it is this class's job and nothing
        /// outside needs <see cref="TerminalViewport.UpdateGeometry"/> — which is the only reason the
        /// concrete type is held at all.
        /// </summary>
        public ITerminalViewport Viewport => viewport;

        /// <summary>Moves the viewport to <paramref name="rect"/>; true when that is somewhere new.</summary>
        public bool Place(Rect<int> rect)
        {
            viewport.UpdateGeometry(rect.X, rect.Y, rect.Width, rect.Height);

            var moved = _last is not { } last
                || last.X != rect.X || last.Y != rect.Y
                || last.Width != rect.Width || last.Height != rect.Height;

            _last = rect;
            return moved;
        }
    }
}
