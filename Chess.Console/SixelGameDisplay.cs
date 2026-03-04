using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
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

    private readonly uint _cellWidth;
    private readonly uint _cellHeight;
    private readonly MagickImage _image;
    private readonly MagickImageRenderer _imageRenderer;
    private readonly SixelDisplay _display;
    private readonly ConsoleGameRenderer _chrome;

    private int _imageColumns;
    private int _imageRows;

    public GameUI UI { get; private set; }

    public SixelGameDisplay(Game game, uint cellWidth, uint cellHeight)
    {
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;
        _imageColumns = System.Console.WindowWidth - HistoryColumns;
        _imageRows = System.Console.WindowHeight - StatusBarRows;

        var width = (uint)_imageColumns * cellWidth;
        var height = (uint)_imageRows * cellHeight;

        _image = new MagickImage(MagickColors.Black, width, height);
        _imageRenderer = new MagickImageRenderer();
        _display = new SixelDisplay();
        _chrome = new ConsoleGameRenderer(HistoryColumns, System.Console.WindowWidth, System.Console.WindowHeight);

        UI = new GameUI(game, _image.Width, _image.Height,
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
            alignment: (cellWidth, cellHeight));
    }

    public void RenderInitial(Game game, File? pendingFile)
    {
        _display.RenderFrame(UI, _imageRenderer, _image, default, _cellHeight);
        _chrome.RenderStatusBar(game, _display.Stats, pendingFile);
        _chrome.RenderHistory(game);
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh))
        {
            _display.RenderFrame(UI, _imageRenderer, _image, clipRects, _cellHeight);
        }
        if (response.HasFlag(UIResponse.IsUpdate))
        {
            _chrome.RenderStatusBar(game, _display.Stats, pendingFile);
            _chrome.RenderHistory(game);
        }
    }

    public void HandleResize(Game game)
    {
        var newConsoleWidth = System.Console.WindowWidth;
        var newConsoleHeight = System.Console.WindowHeight;

        if (!_chrome.NeedsResize(newConsoleWidth, newConsoleHeight))
            return;

        _imageColumns = newConsoleWidth - HistoryColumns;
        _imageRows = newConsoleHeight - StatusBarRows;
        var width = (uint)_imageColumns * _cellWidth;
        var height = (uint)_imageRows * _cellHeight;

        _image.Read(MagickColors.Black, width, height);

        UI = UI.Resize(_image.Width, _image.Height);

        _chrome.Resize(newConsoleWidth, newConsoleHeight);

        _display.RenderFrame(UI, _imageRenderer, _image, default, _cellHeight);
        _chrome.RenderStatusBar(game, _display.Stats);
        _chrome.RenderHistory(game);
    }

    public void Dispose()
    {
        _display.Dispose();
        _imageRenderer.Dispose();
        _image.Dispose();
    }
}
