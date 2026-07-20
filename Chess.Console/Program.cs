using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.UCI;
using DIR.Lib;
using Console.Lib;
using System.CommandLine;

using File = Chess.Lib.File;

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

var renderFenOption = new Option<string?>("--render-fen")
{
    Description = "Render a FEN position as Sixel to stdout and exit"
};

var renderSizeOption = new Option<uint>("--render-size")
{
    Description = "Pixel size for --render-fen output (default: 480)",
    DefaultValueFactory = _ => 480
};

var renderMoveOption = new Option<string?>("--move")
{
    Description = "UCI move to display as arrow overlay (e.g. 'e2e4')"
};

var rootCommand = new RootCommand("Terminal chess game with Sixel graphics")
{
    noColorOption,
    modeOption,
    sideOption,
    boardOption,
    renderFenOption,
    renderSizeOption,
    renderMoveOption
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
    var renderFen = parseResult.GetValue(renderFenOption);
    if (renderFen is not null)
    {
        var size = parseResult.GetValue(renderSizeOption);
        var board = string.Equals(renderFen, "startpos", StringComparison.OrdinalIgnoreCase)
            ? Board.StandardBoard
            : Board.FromFenPlacement(renderFen);

        var game = new Game(board, Side.White, []);
        var renderer = new SixelRgbaImageRenderer(size, size);
        var ui = new GameUI(game, size, size,
            mainFontColor: GameUI.PlainFontColor,
            backgroundColor: GameUI.PlainBackgroundColor);

        // Parse optional --move for arrow overlay
        var moveArg = parseResult.GetValue(renderMoveOption);
        if (moveArg is { Length: 4 or 5 })
        {
            var fromFile = (File)(moveArg[0] - 'a');
            var fromRank = (Rank)(moveArg[1] - '1');
            var toFile = (File)(moveArg[2] - 'a');
            var toRank = (Rank)(moveArg[3] - '1');
            var from = new Position(fromFile, fromRank);
            var to = new Position(toFile, toRank);
            var targetPiece = board[to];
            var isCapture = targetPiece != Piece.None;
            ui.ExplicitArrow = (from, to, isCapture);
        }

        var clip = new RectInt(((int)size, (int)size), PointInt.Origin);
        ui.Render<RgbaImage, SixelRgbaImageRenderer>(renderer, clip);

        using var stdout = System.Console.OpenStandardOutput();
        renderer.EncodeSixel(stdout);
        stdout.WriteByte((byte)'\n');

        renderer.Dispose();
        return;
    }

    var timeProvider = TimeProvider.System;
    await using var terminal = new VirtualTerminal();
    await terminal.InitAsync();

    var imageCapability = parseResult.GetValue(noColorOption)
        ? ImageDisplayCapability.NoColor
        : terminal.ImageDisplayCapability;

    if (imageCapability is ImageDisplayCapability.Sixel)
    {
        terminal.EnterAlternateScreen();
    }

    var restart = true;
    while (restart && !cancellationToken.IsCancellationRequested)
    {
        GameMode gameMode;
        Side computerSide;
        Side sideToMove;

        var modeArg = parseResult.GetValue(modeOption);
        if (modeArg is null)
        {
            var startupMenu = new StartupMenu(terminal, timeProvider);
            (gameMode, computerSide, sideToMove) = await startupMenu.ShowAsync(cancellationToken);
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

            sideToMove = Side.White; // TODO: add --side-to-move CLI option for custom mode
        }

        // Network game: run the LAN lobby to discover + connect a peer, then play them over the
        // socket. A null session means the user backed out of the lobby -> reshow the menu.
        NetworkSession? netSession = null;
        if (gameMode is GameMode.NetworkGame)
        {
            var lanDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpAstro.Chess");
            var preferredColor = computerSide == Side.White ? Side.Black : Side.White;
            netSession = await new ConsoleLanLobby(terminal, timeProvider, lanDir, preferredColor)
                .ShowAsync(cancellationToken);
            if (netSession is null) { restart = true; continue; }
            computerSide = netSession.RemoteSide;
            sideToMove = Side.White;
        }

        Func<IGameDisplay> displayFactory = () =>
            imageCapability is ImageDisplayCapability.Sixel ? new SixelGameDisplay(terminal) : new AsciiDisplay(terminal);

        GameLoop gameLoop;
        if (netSession is { } session)
        {
            gameLoop = new GameLoop(
                timeProvider,
                displayFactory,
                () => new LocalNetworkPlayer(new HumanPlayer(terminal), session),
                (_, _) => new NetworkPlayer(session)
            );
        }
        else
        {
            gameLoop = new GameLoop(
                timeProvider,
                displayFactory,
                () => new HumanPlayer(terminal),
                (cs, tp) => new UciPlayer(UciPlayer.DefaultEnginePath, cs, tp)
            );
        }

        restart = await gameLoop.RunAsync(gameMode, computerSide, sideToMove, cancellationToken);
    }
});

return await rootCommand.Parse(args).InvokeAsync();
