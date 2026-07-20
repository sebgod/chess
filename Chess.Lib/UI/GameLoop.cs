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
        CancellationToken cancellationToken,
        Game? resumeGame = null
    )
    {
        // resumeGame (Continue): use the loaded game as-is so its full ply history drives both the
        // display's move list/playback AND the engine, which rebuilds "position ... moves ..." from
        // the live plies each turn. Reset (F9) still rebuilds from the mode's baseline below, so
        // "New game" always means a fresh standard game, never the resumed midpoint.
        var game = resumeGame
            ?? (gameMode is GameMode.CustomGameEmpty
                ? new Game(new Board(), sideToMove, [])
                : new Game());

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
        IEngineBasedPlayer? opponent;

        var humanPlayer = playerFactory();
        // The "other" player is engine-shaped for PvC/custom AND for a LAN game: a remote peer is a
        // drop-in IEngineBasedPlayer (Chess.Net.NetworkPlayer) here, with computerSide = the peer's
        // colour, so the pairing below wires the local human to the local colour automatically.
        if (gameMode is GameMode.PlayerVsComputer or GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard or GameMode.NetworkGame)
        {
            opponent = engineBasedPlayerFactory(computerSide, timeProvider);

            await opponent.InitAsync(initialFen, cancellationToken);

            if (computerSide is Side.White)
            {
                (whitePlayer, blackPlayer) = (opponent, humanPlayer);
            }
            else
            {
                (whitePlayer, blackPlayer) = (humanPlayer, opponent);
            }
        }
        else
        {
            opponent = null;
            (whitePlayer, blackPlayer) = (humanPlayer, humanPlayer);
        }

        // Orient the board to the local player's colour (their pieces at the bottom) when there's a
        // single local human facing an engine/remote opponent; hot-seat (PvP) stays White-at-bottom.
        // computerSide is the opponent's colour, so the local human is the opposite — flip exactly
        // when the opponent plays White. Ctrl+F still overrides at runtime.
        var flipForLocalSide = opponent is not null && computerSide is Side.White;
        gameDisplay.UI.FlipBoard = flipForLocalSide;

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
                        gameDisplay.UI.FlipBoard = flipForLocalSide; // ResetGame builds a fresh GameUI
                        gameDisplay.RenderInitial(game);
                        if (opponent is not null)
                        {
                            await opponent.NewGameAsync(initialFen, cancellationToken);
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
            if (opponent is not null) await opponent.DisposeAsync();
        }

        return false;
    }
}
