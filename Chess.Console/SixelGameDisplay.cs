using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using ImageMagick;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Sixel-based display with graphical board rendering and chrome (status bar, move history).
/// </summary>
internal sealed class SixelGameDisplay : IGameDisplay
{
    private const int HistoryColumns = 24;
    private const int StatusBarRows = 1;

    private readonly IVirtualTerminal _terminal;
    private readonly byte _cellWidth;
    private readonly byte _cellHeight;
    private readonly MagickImage _image;
    private readonly MagickImageRenderer _imageRenderer;
    private readonly SixelDisplay _display;
    private readonly ConsoleGameRenderer _chrome;
    private readonly TerminalLayout _layout;
    private readonly TerminalViewport _boardViewport;
    private readonly TerminalViewport _historyViewport;
    private readonly TerminalViewport _statusBarViewport;

    public GameUI UI { get; private set; }

    public SixelGameDisplay(IVirtualTerminal terminal, Game game)
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

        _image = new MagickImage(MagickColors.Black, width, height);
        _imageRenderer = new MagickImageRenderer();
        _display = new SixelDisplay(_boardViewport);
        _chrome = new ConsoleGameRenderer(_historyViewport, _statusBarViewport);

        UI = new GameUI(game, _image.Width, _image.Height,
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
            alignment: (cell.Width, cell.Height),
            resolveHistoryClick: ResolveHistoryClick)
        {
            HistoryViewportRows = _historyViewport.Size.Height - 1
        };
    }

    private int? ResolveHistoryClick(int px, int py)
    {
        var cellCol = px / (int)_cellWidth - (_terminal.Size.Width - HistoryColumns);
        var cellRow = py / (int)_cellHeight;
        return _chrome.PlyIndexFromCell(cellCol, cellRow, UI.Game.PlyCount, UI.HistoryScrollStart);
    }

    public void RenderInitial(Game game)
    {
        _display.RenderFrame(UI, _imageRenderer, _image, []);
        _chrome.RenderStatusBar(game, _display.Stats, placementSide: SetupPlacementSide, playbackInfo: PlaybackInfo);
        _chrome.RenderHistory(game, HighlightPlyIndex, UI.HistoryScrollStart);
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh))
        {
            _display.RenderFrame(UI, _imageRenderer, _image, clipRects);
        }
        if (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement))
        {
            _chrome.RenderStatusBar(game, _display.Stats, pendingFile, placementSide: SetupPlacementSide, playbackInfo: PlaybackInfo);
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

        _image.Read(MagickColors.Black, width, height);

        UI = UI.Resize(_image.Width, _image.Height);
        UI.HistoryViewportRows = _historyViewport.Size.Height - 1;

        _display.RenderFrame(UI, _imageRenderer, _image, []);
        _chrome.RenderStatusBar(game, _display.Stats, playbackInfo: PlaybackInfo);
        _chrome.RenderHistory(game, HighlightPlyIndex, UI.HistoryScrollStart);
    }

    public void ResetGame(Game game)
    {
        UI = new GameUI(game, _image.Width, _image.Height,
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
            alignment: (_cellWidth, _cellHeight),
            resolveHistoryClick: ResolveHistoryClick);
    }

    public void Dispose()
    {
        _imageRenderer.Dispose();
        _image.Dispose();
    }
}
