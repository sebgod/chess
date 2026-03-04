using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;

using Action = Chess.Lib.Action;

namespace Chess.Console;

/// <summary>
/// An AI player that communicates with a UCI engine process to make moves.
/// </summary>
internal sealed class UciPlayer : IGamePlayer, IDisposable
{
    private readonly UciClient _client;
    private readonly Side _side;
    private Task<UciResponse.BestMove>? _pendingMove;
    private bool _disposed;

    public string? InitialFen { get; set; }

    public UciPlayer(string enginePath, Side side)
    {
        _side = side;
        _client = new UciClient(enginePath);
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        await _client.StartAsync(ct);
        await _client.NewGameAsync(ct);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects)? TryMakeMove(GameUI ui)
    {
        var game = ui.Game;

        if (game.CurrentSide != _side || game.IsFinished)
        {
            return null;
        }

        if (_pendingMove is null)
        {
            var moves = BuildMovesList(game);
            var position = new UciCommand.SetPosition(InitialFen, moves);
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
            return ui.TryPerformAction(action);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }
}
