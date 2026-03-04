using System.Collections.Immutable;
using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;

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
var (gameMode, computerSide, useStandardBoard) = await startupMenu.ShowAsync(cts.Token);

var game = gameMode is GameMode.CustomGame
    ? new Game(useStandardBoard ? Board.StandardBoard : new Board(), Side.White, ImmutableList<RecordedPly>.Empty)
    : new Game();

var (cellWidth, cellHeight) = hasSixel
    ? await terminal.QueryCellSizeAsync() ?? (10u, 20u)
    : (10u, 20u);

using IGameDisplay gameDisplay = hasSixel
    ? new SixelGameDisplay(game, cellWidth, cellHeight)
    : new AsciiDisplay(game);

var humanPlayer = new HumanPlayer(terminal);

// Custom game setup phase
if (gameMode is GameMode.CustomGame)
{
    gameDisplay.UI.IsSetupMode = true;
    gameDisplay.RenderInitial(game, humanPlayer.PendingFile);

    try
    {
        while (!cts.Token.IsCancellationRequested && gameDisplay.UI.IsSetupMode)
        {
            var result = humanPlayer.TryMakeMove(gameDisplay.UI);

            if (result is { } setupResult)
            {
                gameDisplay.RenderMove(game, setupResult.Response, setupResult.ClipRects, humanPlayer.PendingFile);
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
        return;
    }

    // Transition from setup to gameplay: create a fresh game from the setup board
    var setupBoard = game.Board;
    game = new Game(setupBoard, Side.White, ImmutableList<RecordedPly>.Empty);
    gameDisplay.ResetGame(game);
}

IGamePlayer whitePlayer, blackPlayer;
UciPlayer? uciPlayer;

if (gameMode is GameMode.PlayerVsComputer or GameMode.CustomGame)
{
    var engineName = "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : "");
    var enginePath = Path.Combine(AppContext.BaseDirectory, engineName);
    uciPlayer = new UciPlayer(enginePath, computerSide);

    if (gameMode is GameMode.CustomGame)
    {
        uciPlayer.InitialFen = game.Board.ToFEN() + " w - - 0 1";
    }

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
