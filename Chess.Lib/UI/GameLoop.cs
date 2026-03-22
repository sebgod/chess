namespace Chess.Lib.UI;

public class GameLoop(
    TimeProvider timeProvider,
    Func<IGameDisplay> displayFactory,
    Func<IGamePlayer> playerFactory,
    Func<Side, TimeProvider, IEngineBasedPlayer> engineBasedPlayerFactory
)
{
    public async Task<bool> RunAsync(
        GameMode gameMode,
        Side computerSide,
        Side sideToMove,
        CancellationToken cancellationToken
    )
    {
        var game = gameMode is GameMode.CustomGameEmpty
            ? new Game(new Board(), sideToMove, [])
            : new Game();

        using var gameDisplay = displayFactory();
        gameDisplay.ResetGame(game);

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
                        gameDisplay.RenderMove(game, setupResult.Response, setupResult.ClipRects);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(16), timeProvider, cancellationToken);
                    }

                    gameDisplay.HandleResize(game);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            // Transition from setup to gameplay: create a fresh game from the setup board
            var setupBoard = game.Board;
            game = new Game(setupBoard, sideToMove, []);
            gameDisplay.ResetGame(game);
        }

        var initialBoard = game.Board;
        var sideToMoveChar = sideToMove == Side.Black ? "b" : "w";
        var initialFen = gameMode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard
            ? initialBoard.ToFEN() + $" {sideToMoveChar} - - 0 1"
            : null;

        IGamePlayer whitePlayer, blackPlayer;
        IEngineBasedPlayer? uciPlayer;

        var humanPlayer = playerFactory();
        if (gameMode is GameMode.PlayerVsComputer or GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard)
        {
            uciPlayer = engineBasedPlayerFactory(computerSide, timeProvider);

            await uciPlayer.InitAsync(initialFen, cancellationToken);

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
                var currentPlayer = gameDisplay.UI.Mode == GameUIMode.Playback || game.IsFinished
                    ? humanPlayer
                    : (game.CurrentSide == Side.White ? whitePlayer : blackPlayer);
                var result = currentPlayer.TryMakeMove(gameDisplay.UI);

                if (result is { } moveResult)
                {
                    if (moveResult.Response.HasFlag(UIResponse.NeedsRestart))
                    {
                        return true;
                    }

                    if (moveResult.Response.HasFlag(UIResponse.NeedsReset))
                    {
                        game = initialFen is null
                            ? new Game()
                            : new Game(initialBoard, sideToMove, []);
                        gameDisplay.ResetGame(game);
                        gameDisplay.RenderInitial(game);
                        if (uciPlayer is not null)
                        {
                            await uciPlayer.NewGameAsync(initialFen, cancellationToken);
                        }
                        continue;
                    }

                    gameDisplay.RenderMove(game, moveResult.Response, moveResult.ClipRects);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(16), timeProvider, cancellationToken);
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

        return false;
    }
}
