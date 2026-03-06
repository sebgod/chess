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
    private readonly uint _cellWidth;
    private readonly uint _cellHeight;
    private readonly MagickImage _image;
    private readonly MagickImageRenderer _imageRenderer;
    private readonly SixelDisplay _display;
    private readonly ConsoleGameRenderer _chrome;

    private int _imageColumns;
    private int _imageRows;

    public GameUI UI { get; private set; }

    public SixelGameDisplay(IVirtualTerminal terminal, Game game, uint cellWidth, uint cellHeight)
    {
        _terminal = terminal;
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;

        var (consoleWidth, consoleHeight) = terminal.Size;
        _imageColumns = consoleWidth - HistoryColumns;
        _imageRows = consoleHeight - StatusBarRows;

        var width = (uint)_imageColumns * cellWidth;
        var height = (uint)_imageRows * cellHeight;

        _image = new MagickImage(MagickColors.Black, width, height);
        _imageRenderer = new MagickImageRenderer();
        _display = new SixelDisplay(terminal);
        _chrome = new ConsoleGameRenderer(terminal, HistoryColumns, consoleWidth, consoleHeight);

        UI = new GameUI(game, _image.Width, _image.Height,
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
            alignment: (cellWidth, cellHeight),
            resolveHistoryClick: ResolveHistoryClick);
        UI.HistoryViewportRows = consoleHeight - 2;
    }

    private int? ResolveHistoryClick(int px, int py) =>
        _chrome.PlyIndexFromPixel(px, py, _cellWidth, _cellHeight, UI.Game.PlyCount, UI.HistoryScrollStart);

    public void RenderInitial(Game game)
    {
        _display.RenderFrame(UI, _imageRenderer, _image, [], _cellHeight);
        _chrome.RenderStatusBar(game, _display.Stats, placementSide: SetupPlacementSide, playbackInfo: PlaybackInfo);
        _chrome.RenderHistory(game, HighlightPlyIndex, UI.HistoryScrollStart);
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh))
        {
            _display.RenderFrame(UI, _imageRenderer, _image, clipRects, _cellHeight);
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
        var (newConsoleWidth, newConsoleHeight) = _terminal.Size;

        if (!_chrome.NeedsResize(newConsoleWidth, newConsoleHeight))
            return;

        _imageColumns = newConsoleWidth - HistoryColumns;
        _imageRows = newConsoleHeight - StatusBarRows;
        var width = (uint)_imageColumns * _cellWidth;
        var height = (uint)_imageRows * _cellHeight;

        _image.Read(MagickColors.Black, width, height);

        UI = UI.Resize(_image.Width, _image.Height);
        UI.HistoryViewportRows = newConsoleHeight - 2;

        _chrome.Resize(newConsoleWidth, newConsoleHeight);

        _display.RenderFrame(UI, _imageRenderer, _image, [], _cellHeight);
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
