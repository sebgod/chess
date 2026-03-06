using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.UCI;

/// <summary>
/// An AI player that communicates with a UCI engine process to make moves.
/// </summary>
public sealed class UciPlayer(string enginePath, Side side, TimeProvider timeProvider) : IEngineBasedPlayer
{
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
            var moves = BuildMovesList(game);
            var position = new UciCommand.SetPosition(_initialFen, moves);
            var go = new UciCommand.Go(MoveTime: 1000);
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

    private static string[] BuildMovesList(Game game)
    {
        var plies = game.Plies;
        var moves = new string[plies.Count];

        for (var i = 0; i < plies.Count; i++)
        {
            var ply = plies[i];
            var action = ply.Promoted is not PieceType.None
                ? Action.Promote(ply.From, ply.To, ply.Promoted)
                : Action.DoMove(ply.From, ply.To);
            moves[i] = UciMove.Format(action);
        }

        return moves;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _client.DisposeAsync();
    }
}
