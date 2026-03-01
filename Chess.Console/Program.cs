using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var terminal = new ConsoleTerminal();

// Query cell size before entering alternate buffer to keep response invisible
var (cellWidth, cellHeight) = await terminal.QueryCellSizeAsync() ?? (10, 20);

terminal.Enter();

const int historyColumns = 24;
const int statusBarRows = 1;

var imageColumns = Console.WindowWidth - historyColumns;
var imageRows = Console.WindowHeight - statusBarRows;
var width = (uint)imageColumns * (uint)cellWidth;
var height = (uint)imageRows * (uint)cellHeight;
var game = new Game();
using var image = new MagickImage(MagickColors.Black, width, height);

using var imageRenderer = new MagickImageRenderer();
var ui = new GameUI(game, image.Width, image.Height,
    mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
    backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff));

using var display = new SixelDisplay();
var chrome = new ConsoleGameRenderer(
    historyStartColumn: imageColumns,
    historyColumnWidth: historyColumns,
    historyRowCount: imageRows,
    statusBarRow: Console.WindowHeight - 1,
    totalWidth: Console.WindowWidth);

display.RenderFrame(ui, imageRenderer, image, default, cellHeight);
chrome.RenderStatusBar(game);
chrome.RenderHistory(game);

while (!cts.Token.IsCancellationRequested)
{
    if (terminal.HasInput())
    {
        var mouseEvent = terminal.TryReadMouseEvent();
        if (mouseEvent is { Button: 0, IsRelease: false })
        {
            var pixelX = mouseEvent.Value.X * cellWidth;
            var pixelY = mouseEvent.Value.Y * cellHeight;

            var (response, clipRects) = ui.TryPerformAction(pixelX, pixelY);
            if (response.HasFlag(UIResponse.NeedsRefresh))
            {
                display.RenderFrame(ui, imageRenderer, image, clipRects, cellHeight);
                if (response.HasFlag(UIResponse.IsUpdate))
                {
                    chrome.RenderStatusBar(game);
                    chrome.RenderHistory(game);
                }
            }
        }
    }
    else
    {
        try
        {
            await Task.Delay(16, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}