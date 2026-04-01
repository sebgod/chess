using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;

#if DEBUG
using System.Diagnostics;
#endif

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Base class for graphical game displays that render via a <see cref="Renderer{TSurface}"/>
/// and output Sixel to the terminal.
/// Handles layout, chrome (status bar + move history), GameUI management, and resize logic.
/// </summary>
internal abstract class ConsoleGameDisplayBase<TSurface> : IGameDisplay
{
    /// <summary>
    /// Snapshot of rendering performance counters.
    /// </summary>
    private readonly record struct RenderStats(double LastFrameMs, long FullRenders, long PartialRenders);

    private const int HistoryColumns = 24;
    private const int StatusBarRows = 1;

    private readonly Panel _panel;
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
        _panel = new Panel(terminal);

        _statusBar = new TextBar(_panel.Dock(DockStyle.Bottom, StatusBarRows));
        _historyList = new ScrollableList<HistoryMoveRow>(_panel.Dock(DockStyle.Right, HistoryColumns))
            .Header(" Move History");

        var boardViewport = _panel.Fill();
        var (width, height) = boardViewport.PixelSize;
        var (renderer, encoder) = CreateRenderer(width, height);
        _renderer = renderer;
        _boardCanvas = new Canvas(boardViewport, encoder);

        _panel.Add(_statusBar).Add(_historyList).Add(_boardCanvas);
    }

    protected abstract (Renderer<TSurface> Renderer, ISixelEncoder Encoder) CreateRenderer(uint width, uint height);

    private RenderStats? Stats =>
#if DEBUG
        new(_lastFrameMs, _fullRenders, _partialRenders);
#else
        null;
#endif

    private int? ResolveHistoryClick(int px, int py)
    {
        if (_historyList.HitTest(px, py) is not (var cellCol, var cellRow))
            return null;

        // Row 0 is the header
        if (cellRow < 1)
            return null;

        var plyCount = UI.Game.PlyCount;
        var moveCount = (plyCount + 1) / 2;
        var visibleRows = _historyList.VisibleRows;
        var startMove = UI.HistoryScrollStart ?? Math.Max(0, moveCount - visibleRows);
        var moveIdx = startMove + cellRow - 1;
        var whitePlyIdx = moveIdx * 2;

        if (whitePlyIdx >= plyCount)
            return null;

        var midCol = _historyList.Viewport.Size.Width / 2;
        if (cellCol >= midCol && whitePlyIdx + 1 < plyCount)
            return whitePlyIdx + 1;

        return whitePlyIdx;
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

    private Side? SetupPlacementSide => UI.IsSetupMode ? UI.PlacementSide : null;

    private (int PlyIndex, int PlyCount)? PlaybackInfo => UI.Mode == GameUIMode.Playback
        ? (UI.PlaybackPlyIndex, UI.Game.PlyCount)
        : null;

    private int? HighlightPlyIndex => UI.Mode == GameUIMode.Playback ? UI.PlaybackPlyIndex : null;

    private void UpdateStatusBar(Game game)
    {
        var fileInfo = UI.PendingFile is { } f ? $" [{f.ToLabel()}]" : "";
        var setupInfo = SetupPlacementSide is { } side ? $" Setup: placing {side} pieces [Tab to toggle; s to start]" : "";
        string status;
        if (PlaybackInfo is (var plyIdx, var plyCount))
        {
            status = $" Playback: ply {plyIdx + 2}/{plyCount + 1} [Ctrl+Up/Down, Esc exit]";
        }
        else if (SetupPlacementSide is { })
        {
            status = $" {setupInfo}{fileInfo}";
        }
        else
        {
            status = $" {game.GameStatus.ToMessage(game.CurrentSide)}{fileInfo}";
        }

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
        var moveCount = (plies.Count + 1) / 2;
        var visibleRows = _historyList.VisibleRows;
        var startMove = UI.HistoryScrollStart ?? Math.Max(0, moveCount - visibleRows);
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
        if (!_panel.Recompute())
            return;

        var (width, height) = _boardCanvas.PixelSize;
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
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
            alignment: (cell.Width, cell.Height),
            resolveHistoryClick: ResolveHistoryClick);
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
}
