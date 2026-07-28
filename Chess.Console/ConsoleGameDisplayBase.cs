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
// Layout.Builder -- the same alias PixelGameDisplay uses for the same tree.
using Layout = DIR.Lib.Layout;

namespace Chess.Console;

/// <summary>
/// Base class for graphical game displays that render via a <see cref="Renderer{TSurface}"/>
/// and output Sixel to the terminal.
/// Handles layout, chrome (status bar + move history), GameUI management, and resize logic.
///
/// <para>The frame is declared as a <see cref="Layout.Node"/> tree and arranged in cells by
/// <see cref="Layout.Engine"/> — the same surface-agnostic tree <c>PixelGameDisplay</c> arranges for the
/// GUI and the browser, so the console is no longer the one front-end describing its chrome a different
/// way. Each widget is registered against a key, appears in the tree as a <c>Fill</c> leaf, and has its
/// viewport re-pointed at that leaf's arranged rect. The tree owns placement; the widgets own behaviour —
/// a <see cref="ScrollableList{T}"/> has scroll state and a thumb, a <see cref="Canvas"/> has Sixel dirty
/// regions, and neither is something a layout node can model.</para>
///
/// <para><b>Arranged on resize, not per frame</b>, which is where this deliberately parts company with
/// TianWen's TUI tabs. Every leaf here is a hosted widget — there is no node-painted chrome, no
/// background or label for <see cref="CellLayout.Paint"/> to draw — and the shape depends only on the
/// terminal size, never on game state. Re-arranging per frame would therefore paint nothing new while
/// repainting all three widgets, which is exactly what the clip-rect partial Sixel updates in
/// <see cref="RenderFrame"/> exist to avoid.</para>
/// </summary>
internal abstract class ConsoleGameDisplayBase<TSurface> : IGameDisplay
{
    /// <summary>
    /// Snapshot of rendering performance counters.
    /// </summary>
    private readonly record struct RenderStats(double LastFrameMs, long FullRenders, long PartialRenders);

    private const int HistoryColumns = 24;
    private const int StatusBarRows = 1;

    // Fill-leaf keys. The tree names these; each one's arranged rect becomes a widget's viewport.
    private const string BoardKey = "board";
    private const string HistoryKey = "history";
    private const string StatusKey = "status";

    /// <summary>Stateless — text width is the character count — so one instance serves every arrange.</summary>
    private static readonly CellMeasureContext MeasureContext = new();

    private readonly IVirtualTerminal _terminal;
    private readonly Dictionary<string, HostedRegion> _hosts = [];
    private readonly Canvas _boardCanvas;
    private readonly Renderer<TSurface> _renderer;
    private readonly TextBar _statusBar;
    private readonly ScrollableList<HistoryMoveRow> _historyList;

    private GameUI? _gameUI;

#if DEBUG
    private readonly Stopwatch _stopwatch = new();
    private double _lastFrameMs;
    private long _fullRenders;
    private long _partialRenders;
#endif

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    protected ConsoleGameDisplayBase(IVirtualTerminal terminal)
    {
        _terminal = terminal;

        _statusBar = new TextBar(Host(StatusKey));
        _historyList = new ScrollableList<HistoryMoveRow>(Host(HistoryKey))
            .Header(" Move History");
        ITerminalViewport boardViewport = Host(BoardKey);

        // A hosted viewport's geometry is meaningless until the tree places it, and the renderer needs a
        // pixel size, so the first arrange has to happen before the board can be built.
        ArrangeFrame();

        var (width, height) = boardViewport.PixelSize;
        var (renderer, encoder) = CreateRenderer(width, height);
        _renderer = renderer;
        _boardCanvas = new Canvas(boardViewport, encoder);
    }

    protected abstract (Renderer<TSurface> Renderer, ISixelEncoder Encoder) CreateRenderer(uint width, uint height);

    private RenderStats? Stats =>
#if DEBUG
        new(_lastFrameMs, _fullRenders, _partialRenders);
#else
        null;
#endif

    /// <summary>
    /// Creates the viewport for the widget hosted at <paramref name="key"/>. Its geometry stays empty
    /// until <see cref="ArrangeFrame"/> places it.
    /// </summary>
    private TerminalViewport Host(string key)
    {
        var viewport = new TerminalViewport(_terminal, 0, 0, 0, 0);
        _hosts[key] = new HostedRegion(viewport);
        return viewport;
    }

    /// <summary>
    /// The frame: the board with the history panel beside it, and a status bar spanning the full width
    /// below both.
    ///
    /// <para>The tree is rebuilt per arrange, so the clamps are plain C# rather than layout features. They
    /// preserve what the docked <c>Panel</c> did before, where <c>TerminalLayout</c> clamped each strip to
    /// the cells still remaining: in a terminal too narrow for the history panel, the panel takes what is
    /// there and the board is left with nothing rather than the frame overflowing.</para>
    /// </summary>
    /// <remarks>
    /// <b>Every leaf states BOTH axes.</b> In an <c>HStack</c> the cross axis is the height, and a
    /// <c>Fill</c> leaf has no intrinsic size, so a child that says only <c>.WFixed()</c> or
    /// <c>.WStar()</c> keeps <see cref="Layout.Node.Height"/> at <c>Auto</c>, measures its
    /// <c>MinHeight</c> — zero — and is arranged <b>zero rows tall</b>. The panel then vanishes rather
    /// than the frame looking wrong, which is why the tests assert on arranged capacity.
    /// <para><c>ColW</c>/<c>RowH</c>/<c>Stretch</c> exist to make that pairing unforgettable: each sets
    /// both axes at once. Prefer them over spelling out <c>.WFixed().HStar()</c>, which reads as two
    /// independent choices when it is really one decision.</para>
    /// </remarks>
    private Layout.Node BuildLayout(int columns, int rows) =>
        Layout.Builder.VStack(
            Layout.Builder.HStack(
                Layout.Builder.Fill(key: BoardKey).Stretch(),
                Layout.Builder.Fill(key: HistoryKey).ColW(Math.Min(HistoryColumns, columns)))
                .Stretch(),
            Layout.Builder.Fill(key: StatusKey).RowH(Math.Min(StatusBarRows, rows)));

    /// <summary>
    /// Re-arranges the frame and re-points every hosted viewport at its new rect. Returns whether any of
    /// them actually moved — the replacement for <c>Panel.Recompute()</c>'s "did the terminal change"
    /// guard, which is what keeps a per-pump <see cref="HandleResize"/> from repainting continuously.
    /// </summary>
    private bool ArrangeFrame()
    {
        var (columns, rows) = _terminal.Size;
        var arranged = Layout.Engine.Arrange(
            BuildLayout(columns, rows), new Rect<int>(0, 0, columns, rows), MeasureContext);

        var moved = false;
        foreach (var (node, rect) in arranged)
        {
            if (node is Layout.Node.Leaf { Content: Layout.Content.Fill { Key: { } key } }
                && _hosts.TryGetValue(key, out var host))
            {
                moved |= host.Place(rect);
            }
        }

        return moved;
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
                debugInfo = $"{s.LastFrameMs,6:F1}ms  F:{s.FullRenders} P:{s.PartialRenders} ({100.0 * s.PartialRenders / total:F0}% partial) ";
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
        _gameUI = UI.Resize(width, height);

        UI.HistoryViewportRows = _historyList.VisibleRows;

        RenderFrame(UI, []);
        UpdateStatusBar(game);
        UpdateHistory(game);
    }

    public void ResetGame(Game game)
    {
        var cell = _boardCanvas.Viewport.CellSize;
        _gameUI = new GameUI(game, _renderer.Width, _renderer.Height,
            mainFontColor: GameUI.PlainFontColor,
            backgroundColor: GameUI.PlainBackgroundColor,
            alignment: (cell.Width, cell.Height),
            resolveHistoryClick: ResolveHistoryClick);

        // HandleResize is the other writer, but it only runs its body when the arrangement CHANGED, so a
        // game that is never resized would leave this at 0 -- and every scroll path divides the history
        // by it (ScrollHistory, PageUp/PageDown, EnsurePlyVisible). PixelGameDisplay sets it from its own
        // arrange for the same reason.
        _gameUI.HistoryViewportRows = _historyList.VisibleRows;
    }

    private void RenderFrame(GameUI ui, ImmutableArray<RectInt> clipRects)
    {
#if DEBUG
        _stopwatch.Restart();
#endif

        var renderer = _renderer;
        RectInt clip;
        bool isPartial;
        if (!clipRects.IsDefault && clipRects.Length > 0)
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

        ui.Render<TSurface, Renderer<TSurface>>(renderer, clip);

        if (isPartial)
            _boardCanvas.Render(clip);
        else
            _boardCanvas.Render();

#if DEBUG
        _stopwatch.Stop();
        _lastFrameMs = _stopwatch.Elapsed.TotalMilliseconds;
        if (isPartial) _partialRenders++; else _fullRenders++;
#endif
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
