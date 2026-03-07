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
/// Snapshot of rendering performance counters.
/// </summary>
internal readonly record struct RenderStats(double LastFrameMs, long FullRenders, long PartialRenders);

/// <summary>
/// Base class for graphical game displays that render via a <see cref="Renderer{TSurface}"/>
/// and output Sixel to the terminal.
/// Handles layout, chrome (status bar + move history), GameUI management, and resize logic.
/// </summary>
internal abstract class ConsoleGameDisplayBase<TSurface> : IGameDisplay
{
    private const int HistoryColumns = 24;
    private const int StatusBarRows = 1;

    private readonly IVirtualTerminal _terminal;
    private readonly byte _cellWidth;
    private readonly byte _cellHeight;
    private readonly Panel _panel;
    private readonly Canvas _boardCanvas;
    private readonly TextBar _statusBar;
    private readonly ScrollableList<HistoryMoveRow> _historyList;
    private readonly Renderer<TSurface> _renderer;

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

        var cell = terminal.CellSize;
        _cellWidth = cell.Width;
        _cellHeight = cell.Height;

        _statusBar = new TextBar().Style("\e[97;100m");
        _historyList = new ScrollableList<HistoryMoveRow>()
            .Header(" Move History")
            .HeaderStyle("\e[97;100m")
            .EmptyStyle("\e[37;40m");
        _boardCanvas = new Canvas();

        _panel = new Panel(terminal)
            .Dock(DockStyle.Bottom, StatusBarRows, _statusBar)
            .Dock(DockStyle.Right, HistoryColumns, _historyList)
            .Fill(_boardCanvas);

        var (boardCols, boardRows) = _boardCanvas.Size;
        var width = (uint)boardCols * cell.Width;
        var height = (uint)boardRows * cell.Height;

        _renderer = CreateRenderer(width, height);
    }

    protected abstract Renderer<TSurface> CreateRenderer(uint width, uint height);
    protected abstract void EncodeSixel(TSurface surface, Stream output);
    protected abstract void EncodeSixel(TSurface surface, int startY, uint height, Stream output);

    private RenderStats? Stats =>
#if DEBUG
        new(_lastFrameMs, _fullRenders, _partialRenders);
#else
        null;
#endif

    private int? ResolveHistoryClick(int px, int py)
    {
        var cellCol = px / (int)_cellWidth - (_terminal.Size.Width - HistoryColumns);
        var cellRow = py / (int)_cellHeight;
        return PlyIndexFromCell(cellCol, cellRow, UI.Game.PlyCount, UI.HistoryScrollStart);
    }

    private int? PlyIndexFromCell(int cellCol, int cellRow, int plyCount, int? scrollStart)
    {
        var historyRowCount = _historyList.Viewport.Size.Height;

        if (cellCol < 0 || cellRow < 1 || cellRow >= historyRowCount)
            return null;

        var moveCount = (plyCount + 1) / 2;
        var startMove = scrollStart ?? Math.Max(0, moveCount - (historyRowCount - 1));
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

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh))
        {
            RenderFrame(UI, clipRects);
        }
        if (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement))
        {
            UpdateStatusBar(game, pendingFile);
            UpdateHistory(game);
        }
    }

    private Side? SetupPlacementSide => UI.IsSetupMode ? UI.PlacementSide : null;

    private (int PlyIndex, int PlyCount)? PlaybackInfo => UI.Mode == GameUIMode.Playback
        ? (UI.PlaybackPlyIndex, UI.Game.PlyCount)
        : null;

    private int? HighlightPlyIndex => UI.Mode == GameUIMode.Playback ? UI.PlaybackPlyIndex : null;

    private void UpdateStatusBar(Game game, File? pendingFile = null)
    {
        var fileInfo = pendingFile is { } f ? $" [{f.ToLabel()}]" : "";
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

        var (boardCols, boardRows) = _boardCanvas.Size;
        var width = (uint)boardCols * _cellWidth;
        var height = (uint)boardRows * _cellHeight;

        _renderer.Resize(width, height);
        _gameUI = UI.Resize(width, height);

        UI.HistoryViewportRows = _historyList.VisibleRows;

        RenderFrame(UI, []);
        UpdateStatusBar(game);
        UpdateHistory(game);
    }

    public void ResetGame(Game game)
    {
        _gameUI = new GameUI(game, _renderer.Width, _renderer.Height,
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
            alignment: (_cellWidth, _cellHeight),
            resolveHistoryClick: ResolveHistoryClick);
    }

    private void RenderFrame(GameUI ui, ImmutableArray<RectInt> clipRects)
    {
#if DEBUG
        _stopwatch.Restart();
#endif

        var surface = _renderer.Surface;
        RectInt clip;
        bool isFullRender;
        if (!clipRects.IsDefault && clipRects.Length > 0)
        {
            isFullRender = false;
            clip = clipRects[0];
            for (var i = 1; i < clipRects.Length; i++)
            {
                clip = clip.Union(clipRects[i]);
            }
        }
        else
        {
            isFullRender = true;
            clip = new RectInt((_renderer.Width, _renderer.Height), PointInt.Origin);
        }

        ui.Render<TSurface, Renderer<TSurface>>(_renderer, clip);

        if (isFullRender)
        {
            _boardCanvas.SetCursorPosition(0, 0);
            EncodeSixel(surface, _boardCanvas.OutputStream);
        }
        else
        {
            var startRow = clip.UpperLeft.Y / _cellHeight;
            var endRow = (clip.LowerRight.Y + _cellHeight - 1) / _cellHeight;

            var pixelStartY = startRow * _cellHeight;
            var pixelEndY = Math.Min(_renderer.Height, endRow * _cellHeight);
            var cropHeight = pixelEndY - pixelStartY;

            if (cropHeight > 0)
            {
                _boardCanvas.SetCursorPosition(0, startRow);
                EncodeSixel(surface, pixelStartY, (uint)cropHeight, _boardCanvas.OutputStream);
            }
        }

#if DEBUG
        _stopwatch.Stop();
        _lastFrameMs = _stopwatch.Elapsed.TotalMilliseconds;
        if (isFullRender) _fullRenders++; else _partialRenders++;
#endif
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }
}
