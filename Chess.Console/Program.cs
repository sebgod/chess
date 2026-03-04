using Chess.Console;
using Chess.Lib;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var terminal = new ConsoleTerminal();
var hasSixel = await terminal.HasSixelSupportAsync();

if (hasSixel)
{
    await terminal.EnterAsync();
}

var startupMenu = new StartupMenu(terminal);
var (gameMode, computerSide) = await startupMenu.ShowAsync(cts.Token);

var game = new Game();

var (cellWidth, cellHeight) = hasSixel
    ? await terminal.QueryCellSizeAsync() ?? (10u, 20u)
    : (10u, 20u);

using IGameDisplay gameDisplay = hasSixel
    ? new SixelGameDisplay(game, cellWidth, cellHeight)
    : new AsciiDisplay(game);

var humanPlayer = new HumanPlayer(terminal);
IGamePlayer whitePlayer, blackPlayer;
UciPlayer? uciPlayer;

if (gameMode is GameMode.PlayerVsComputer)
{
    var engineName = "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : "");
    var enginePath = Path.Combine(AppContext.BaseDirectory, engineName);
    uciPlayer = new UciPlayer(enginePath, computerSide);
    await uciPlayer.InitAsync(cts.Token);

    if (computerSide is Side.White)
    {
        (whitePlayer, blackPlayer) = (uciPlayer, humanPlayer);
    }
    else
    {
        (whitePlayer, blackPlayer) = (humanPlayer, uciPlayer);
    }
}
else
{
    uciPlayer = null;
    (whitePlayer, blackPlayer) = (humanPlayer, humanPlayer);
}

gameDisplay.RenderInitial(game, humanPlayer.PendingFile);

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var currentPlayer = game.CurrentSide == Side.White ? whitePlayer : blackPlayer;
        var result = currentPlayer.TryMakeMove(gameDisplay.UI);

        if (result is { } moveResult)
        {
            gameDisplay.RenderMove(game, moveResult.Response, moveResult.ClipRects, humanPlayer.PendingFile);
        }
        else
        {
            await Task.Delay(16, cts.Token);
        }

        gameDisplay.HandleResize(game);
    }
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C
}
finally
{
    uciPlayer?.Dispose();
}
