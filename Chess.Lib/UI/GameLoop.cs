namespace Chess.Lib.UI;

/// <summary>
/// The <b>pull</b> driver for <see cref="GameSession"/>: owns a thread, polls the session, paints what
/// it reports, and sleeps when nothing happened. Chess.Console and Chess.GUI use it; the push
/// front-ends (Chess.Droid's SDL callbacks, Chess.Web's browser events) tick the same session from
/// their own loops instead.
///
/// <para>Everything that is actually about chess lives in <see cref="GameSession"/>. What is left here
/// is the loop and the painting — the two things that cannot be shared, because a browser cannot own a
/// blocking loop and its rendering is awaited interop.</para>
/// </summary>
public class GameLoop(
    TimeProvider timeProvider,
    Func<IGameDisplay> displayFactory,
    Func<IGamePlayer> playerFactory,
    Func<Side, TimeProvider, IEngineBasedPlayer> engineBasedPlayerFactory
)
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(16);

    /// <summary>Runs until cancelled or restarted. Returns true when the caller should show the menu again.</summary>
    public async Task<bool> RunAsync(
        GameMode gameMode,
        Side computerSide,
        Side sideToMove,
        CancellationToken cancellationToken,
        Game? resumeGame = null
    )
    {
        using var gameDisplay = displayFactory();

        var session = GameSession.Create(
            gameDisplay,
            gameMode,
            computerSide,
            sideToMove,
            playerFactory,
            engineBasedPlayerFactory,
            resumeGame);

        try
        {
            if (session.IsSetupMode)
            {
                gameDisplay.RenderInitial(session.Game);

                while (!cancellationToken.IsCancellationRequested && session.IsSetupMode)
                {
                    await PumpAsync(session, gameDisplay, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }

            await session.StartAsync(timeProvider, cancellationToken);
            gameDisplay.RenderInitial(session.Game);

            while (!cancellationToken.IsCancellationRequested)
            {
                var tick = await PumpAsync(session, gameDisplay, cancellationToken);

                if (tick.Outcome is SessionOutcome.NeedsRestart)
                {
                    return true;
                }

                if (tick.Outcome is SessionOutcome.NeedsReset)
                {
                    await session.ResetAsync(cancellationToken);
                    gameDisplay.RenderInitial(session.Game);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C
        }
        finally
        {
            await session.DisposeAsync();
        }

        return false;
    }

    /// <summary>
    /// One turn of the pull loop: tick, paint whatever moved, idle if nothing did. Control outcomes
    /// are handed back to the caller rather than acted on here.
    /// </summary>
    private async Task<SessionTick> PumpAsync(
        GameSession session, IGameDisplay gameDisplay, CancellationToken cancellationToken)
    {
        var tick = session.Tick();

        if (tick.Outcome is SessionOutcome.Moved)
        {
            gameDisplay.RenderMove(session.Game, tick.Response, tick.ClipRects);
        }
        else if (tick.Outcome is SessionOutcome.Idle)
        {
            await Task.Delay(IdleDelay, timeProvider, cancellationToken);
        }

        gameDisplay.HandleResize(session.Game);
        return tick;
    }
}
