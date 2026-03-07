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
    private readonly ConsoleGameRenderer _chrome;
    private readonly TerminalLayout _layout;
    private readonly TerminalViewport _boardViewport;
    private readonly TerminalViewport _historyViewport;
    private readonly TerminalViewport _statusBarViewport;
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

        _layout = new TerminalLayout(terminal);
        _statusBarViewport = _layout.Dock(Dock.Bottom, StatusBarRows);
        _historyViewport = _layout.Dock(Dock.Right, HistoryColumns);
        _boardViewport = _layout.Dock(Dock.Fill);

        var (boardCols, boardRows) = _boardViewport.Size;
        var width = (uint)boardCols * cell.Width;
        var height = (uint)boardRows * cell.Height;

        _renderer = CreateRenderer(width, height);
        _chrome = new ConsoleGameRenderer(_historyViewport, _statusBarViewport);
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
        return _chrome.PlyIndexFromCell(cellCol, cellRow, UI.Game.PlyCount, UI.HistoryScrollStart);
    }

    public void RenderInitial(Game game)
    {
        RenderFrame(UI, []);
        _chrome.RenderStatusBar(game, Stats, placementSide: SetupPlacementSide, playbackInfo: PlaybackInfo);
        _chrome.RenderHistory(game, HighlightPlyIndex, UI.HistoryScrollStart);
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh))
        {
            RenderFrame(UI, clipRects);
        }
        if (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement))
        {
            _chrome.RenderStatusBar(game, Stats, pendingFile, placementSide: SetupPlacementSide, playbackInfo: PlaybackInfo);
            _chrome.RenderHistory(game, HighlightPlyIndex, UI.HistoryScrollStart);
        }
    }

    private Side? SetupPlacementSide => UI.IsSetupMode ? UI.PlacementSide : null;

    private (int PlyIndex, int PlyCount)? PlaybackInfo => UI.Mode == GameUIMode.Playback
        ? (UI.PlaybackPlyIndex, UI.Game.PlyCount)
        : null;

    private int? HighlightPlyIndex => UI.Mode == GameUIMode.Playback ? UI.PlaybackPlyIndex : null;

    public void HandleResize(Game game)
    {
        if (!_layout.Recompute())
            return;

        var (boardCols, boardRows) = _boardViewport.Size;
        var width = (uint)boardCols * _cellWidth;
        var height = (uint)boardRows * _cellHeight;

        _renderer.Resize(width, height);
        _gameUI = UI.Resize(width, height);

        UI.HistoryViewportRows = _historyViewport.Size.Height - 1;

        RenderFrame(UI, []);
        _chrome.RenderStatusBar(game, Stats, playbackInfo: PlaybackInfo);
        _chrome.RenderHistory(game, HighlightPlyIndex, UI.HistoryScrollStart);
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
            _boardViewport.SetCursorPosition(0, 0);
            EncodeSixel(surface, _boardViewport.OutputStream);
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
                _boardViewport.SetCursorPosition(0, startRow);
                EncodeSixel(surface, pixelStartY, (uint)cropHeight, _boardViewport.OutputStream);
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
