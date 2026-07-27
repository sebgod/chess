using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.UCI;

/// <summary>
/// An AI player that communicates with a UCI engine process to make moves.
/// </summary>
public sealed class UciPlayer(
    string enginePath,
    Side side,
    TimeProvider timeProvider,
    Difficulty difficulty = Difficulty.Normal) : IEngineBasedPlayer
{
    /// <summary>
    /// The bundled chess-engine executable next to the host binary — the path every desktop
    /// front-end launches by default (was duplicated verbatim in the GUI and Console hosts).
    /// </summary>
    public static string DefaultEnginePath =>
        Path.Combine(AppContext.BaseDirectory, "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    private readonly UciClient _client = new UciClient(enginePath, timeProvider);
    private Task<UciResponse.BestMove>? _pendingMove;
    private string? _initialFen;
    private bool _disposed;

    public async Task InitAsync(string? initialFen, CancellationToken ct = default)
    {
        _initialFen = initialFen;
        await _client.StartAsync(ct);
        await _client.NewGameAsync(ct);
    }

    public async Task NewGameAsync(string? initialFen, CancellationToken ct = default)
    {
        _initialFen = initialFen;
        _pendingMove = null;
        await _client.NewGameAsync(ct);
    }

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        var game = ui.Game;

        if (game.CurrentSide != side || game.IsFinished)
        {
            return null;
        }

        if (_pendingMove is null)
        {
            var moves = UciMove.FormatMoves(game);
            var position = new UciCommand.SetPosition(_initialFen, moves);

            // Depth, not time: an untimed game should answer as fast as the chosen strength allows
            // rather than always spending a fixed think. (This used to ask for a fixed 1000 ms, which
            // the engine ignored outright — now that it honours the clock, asking for time would mean
            // waiting a second for every move of a casual game.) A real time control belongs here as
            // wtime/btime once there is a clock to read it from.
            var go = new UciCommand.Go(Depth: difficulty.ToSearchDepth());
            _pendingMove = _client.GoAsync(position, go);
        }

        if (_pendingMove.IsCompleted)
        {
            var bestMove = _pendingMove.Result;
            _pendingMove = null;

            if (bestMove.Move is "0000")
            {
                return null;
            }

            var action = UciMove.Parse(bestMove.Move);
            var (response, clips) = ui.TryPerformAction(action);
            return new PlayerMoveResult(response, clips);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _client.DisposeAsync();
    }
}
