namespace Chess.Lib.UI;

public class GameLoop(
    TimeProvider timeProvider,
    Func<Game, IGameDisplay> displayFactory,
    Func<IGamePlayer> playerFactory,
    Func<Side, TimeProvider, IEngineBasedPlayer> engineBasedPlayerFactory
)
{
    public async Task RunAsync(
        GameMode gameMode,
        Side computerSide,
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
                        await Task.Delay(TimeSpan.FromMilliseconds(16), timeProvider, cancellationToken);
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
            uciPlayer = engineBasedPlayerFactory(computerSide, timeProvider);

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
                var currentPlayer = gameDisplay.UI.Mode == GameUIMode.Playback
                    ? humanPlayer
                    : (game.CurrentSide == Side.White ? whitePlayer : blackPlayer);
                var result = currentPlayer.TryMakeMove(gameDisplay.UI);

                if (result is { } moveResult)
                {
                    gameDisplay.RenderMove(game, moveResult.Response, moveResult.ClipRects, moveResult.PendingFile);
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
    }
}
