using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using System.CommandLine;

var noColorOption = new Option<bool>("--no-color")
{
    Description = "Force ASCII display (no Sixel graphics)"
};

var modeOption = new Option<string>("--mode", "-m")
{
    Description = "Game mode: pvp, pvc, or custom"
};
modeOption.AcceptOnlyFromAmong("pvp", "pvc", "custom");

var sideOption = new Option<string>("--side", "-s")
{
    Description = "Side to play as: white or black (required for pvc/custom)"
};
sideOption.AcceptOnlyFromAmong("white", "black");

var boardOption = new Option<string>("--board", "-b")
{
    Description = "Starting board for custom mode: empty or standard",
    DefaultValueFactory = _ => "standard"
};
boardOption.AcceptOnlyFromAmong("empty", "standard");

var rootCommand = new RootCommand("Terminal chess game with Sixel graphics")
{
    noColorOption,
    modeOption,
    sideOption,
    boardOption
};

rootCommand.Validators.Add(result =>
{
    string? mode;
    try { mode = result.GetValue(modeOption); }
    catch { return; } // AcceptOnlyFromAmong handles invalid values

    if (mode is "pvc" or "custom" && result.GetValue(sideOption) is null)
    {
        result.AddError("--side is required when --mode is 'pvc' or 'custom'.");
    }
});

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    bool forceAscii = parseResult.GetValue(noColorOption)
        || Environment.GetEnvironmentVariable("NO_COLOR") is "1"
        || Environment.GetEnvironmentVariable("NOCOLOR") is "1";

    using var terminal = new ConsoleTerminal();
    var hasSixel = !forceAscii && await terminal.HasSixelSupportAsync();

    if (hasSixel)
    {
        await terminal.EnterAsync();
    }

    GameMode gameMode;
    Side computerSide;

    var modeArg = parseResult.GetValue(modeOption);
    if (modeArg is null)
    {
        var startupMenu = new StartupMenu(terminal);
        (gameMode, computerSide) = await startupMenu.ShowAsync(cancellationToken);
    }
    else
    {
        var sideArg = parseResult.GetValue(sideOption);
        var boardArg = parseResult.GetValue(boardOption);

        gameMode = modeArg switch
        {
            "pvp" => GameMode.PlayerVsPlayer,
            "pvc" => GameMode.PlayerVsComputer,
            "custom" when boardArg == "empty" => GameMode.CustomGameEmpty,
            "custom" when boardArg != "empty" => GameMode.CustomGameStandardBoard,
            _ => throw new InvalidOperationException()
        };

        computerSide = gameMode is GameMode.PlayerVsPlayer
            ? Side.None
            : (sideArg == "white" ? Side.Black : Side.White);

    }

    var (cellWidth, cellHeight) = hasSixel
        ? await terminal.QueryCellSizeAsync() ?? (10u, 20u)
        : (10u, 20u);

    await RunGameLoopAsync(
        gameMode,
        computerSide,
        game => hasSixel  ? new SixelGameDisplay(game, cellWidth, cellHeight) : new AsciiDisplay(game),
        () => new HumanPlayer(terminal),
        computerSide => new UciPlayer(Path.Combine(AppContext.BaseDirectory, "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : "")), computerSide),
        cancellationToken
    );
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task RunGameLoopAsync(
    GameMode gameMode,
    Side computerSide,
    Func<Game, IGameDisplay> displayFactory,
    Func<IGamePlayer> playerFactory,
    Func<Side, IEngineBasedPlayer> engineBasedPlayerFactory,
    CancellationToken cancellationToken
)
{

    var game = gameMode is GameMode.CustomGameEmpty
        ? new Game(new Board(), Side.White, [])
        : new Game();

    using var gameDisplay = displayFactory(game);

    // Custom game setup phase
    if (gameMode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard)
    {
        var setupPlayer = playerFactory();

        gameDisplay.UI.IsSetupMode = true;
        gameDisplay.RenderInitial(game);

        try
        {
            while (!cancellationToken.IsCancellationRequested && gameDisplay.UI.IsSetupMode)
            {
                var result = setupPlayer.TryMakeMove(gameDisplay.UI);

                if (result is { } setupResult)
                {
                    gameDisplay.RenderMove(game, setupResult.Response, setupResult.ClipRects, setupResult.PendingFile);
                }
                else
                {
                    await Task.Delay(16, cancellationToken);
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
        game = new Game(setupBoard, Side.White, []);
        gameDisplay.ResetGame(game);
    }

    IGamePlayer whitePlayer, blackPlayer;
    IEngineBasedPlayer? uciPlayer;

    var humanPlayer = playerFactory();
    if (gameMode is GameMode.PlayerVsComputer or GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard)
    {
        uciPlayer = engineBasedPlayerFactory(computerSide);

        await uciPlayer.InitAsync(gameMode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard
            ? game.Board.ToFEN() + " w - - 0 1"
            : null,
            cancellationToken
        );

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

    gameDisplay.RenderInitial(game);

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var currentPlayer = game.CurrentSide == Side.White ? whitePlayer : blackPlayer;
            var result = currentPlayer.TryMakeMove(gameDisplay.UI);

            if (result is { } moveResult)
            {
                gameDisplay.RenderMove(game, moveResult.Response, moveResult.ClipRects, moveResult.PendingFile);
            }
            else
            {
                await Task.Delay(16, cancellationToken);
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
        if (uciPlayer is not null) await uciPlayer.DisposeAsync();
    }
}