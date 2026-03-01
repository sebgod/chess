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

await terminal.EnterAsync();

var (gameMode, computerSide) = await StartupMenu.ShowAsync(cts.Token);

const int historyColumns = 24;
const int statusBarRows = 1;

var imageColumns = Console.WindowWidth - historyColumns;
var imageRows = Console.WindowHeight - statusBarRows;
var (cellWidth, cellHeight) = await terminal.QueryCellSizeAsync() ?? (10u, 20u);
var width = (uint)imageColumns * cellWidth;
var height = (uint)imageRows * cellHeight;
var game = new Game();
using var image = new MagickImage(MagickColors.Black, width, height);

using var imageRenderer = new MagickImageRenderer();
var ui = new GameUI(game, image.Width, image.Height,
    mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
    backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff),
    alignment: (cellWidth, cellHeight));

using var display = new SixelDisplay();
var chrome = new ConsoleGameRenderer(historyColumns, Console.WindowWidth, Console.WindowHeight);

var humanPlayer = new HumanPlayer(terminal);
IGamePlayer whitePlayer, blackPlayer;

if (gameMode is GameMode.PlayerVsComputer)
{
    var aiPlayer = new AiPlayer(new AiEngine(computerSide));
    if (computerSide is Side.White)
    {
        (whitePlayer, blackPlayer) = (aiPlayer, humanPlayer);
    }
    else
    {
        (whitePlayer, blackPlayer) = (humanPlayer, aiPlayer);
    }
}
else
{
    (whitePlayer, blackPlayer) = (humanPlayer, humanPlayer);
}

display.RenderFrame(ui, imageRenderer, image, default, cellHeight);
chrome.RenderStatusBar(game, display.Stats);
chrome.RenderHistory(game);

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var currentPlayer = game.CurrentSide == Side.White ? whitePlayer : blackPlayer;
        var result = currentPlayer.TryMakeMove(ui);

        if (result is { } moveResult && moveResult.Response.HasFlag(UIResponse.NeedsRefresh))
        {
            display.RenderFrame(ui, imageRenderer, image, moveResult.ClipRects, cellHeight);
            if (moveResult.Response.HasFlag(UIResponse.IsUpdate))
            {
                chrome.RenderStatusBar(game, display.Stats);
                chrome.RenderHistory(game);
            }
        }
        else if (result is null)
        {
            await Task.Delay(16, cts.Token);
        }

        var newConsoleWidth = Console.WindowWidth;
        var newConsoleHeight = Console.WindowHeight;
        // Detect resizing and recreate UI
        if (chrome.NeedsResize(newConsoleWidth, newConsoleHeight))
        {
            imageColumns = newConsoleWidth - historyColumns;
            imageRows = newConsoleHeight - statusBarRows;
            width = (uint)imageColumns * cellWidth;
            height = (uint)imageRows * cellHeight;

            image.Read(MagickColors.Black, width, height);

            ui = ui.Resize(image.Width, image.Height);

            chrome.Resize(newConsoleWidth, newConsoleHeight);

            display.RenderFrame(ui, imageRenderer, image, default, cellHeight);
            chrome.RenderStatusBar(game, display.Stats);
            chrome.RenderHistory(game);
        }
    }
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C
}