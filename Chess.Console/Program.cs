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

    var gameLoop = new GameLoop(
        game => hasSixel  ? new SixelGameDisplay(game, cellWidth, cellHeight) : new AsciiDisplay(game),
        () => new HumanPlayer(terminal),
        computerSide => new UciPlayer(Path.Combine(AppContext.BaseDirectory, "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : "")), computerSide)
    );

    await gameLoop.RunAsync(gameMode, computerSide, cancellationToken);
});

return await rootCommand.Parse(args).InvokeAsync();