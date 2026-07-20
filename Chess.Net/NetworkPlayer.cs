using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using DIR.Lib;

namespace Chess.Net;

/// <summary>
/// The remote opponent as an <see cref="IEngineBasedPlayer"/> — a drop-in for <c>UciPlayer</c> in the
/// engine-shaped slot of <c>GameLoop</c>. On the remote side's turn it drains one move the peer sent
/// and applies it via <see cref="GameUI.TryPerformAction"/> (the exact poll shape as
/// <c>UciPlayer.TryMakeMove</c>); otherwise it's idle. If the peer leaves, it unwinds to the menu.
/// Owns the <see cref="NetworkSession"/>'s disposal (GameLoop calls DisposeAsync in its finally).
/// </summary>
public sealed class NetworkPlayer(NetworkSession session) : IEngineBasedPlayer
{
    private readonly Side _remoteSide = session.RemoteSide;

    public Task InitAsync(string? initialFen, CancellationToken ct = default) => Task.CompletedTask;

    // A LAN game can't re-sync a local-only reset to the peer, so "new game" is a no-op here (reset
    // is not offered in network mode).
    public Task NewGameAsync(string? initialFen, CancellationToken ct = default) => Task.CompletedTask;

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        if (session.PeerLeft)
            return new PlayerMoveResult(UIResponse.NeedsRestart, ImmutableArray<RectInt>.Empty);

        var game = ui.Game;
        if (game.CurrentSide != _remoteSide || game.IsFinished)
            return null;

        if (!session.TryDequeueMove(out var uci))
            return null;

        var action = UciMove.Parse(uci);
        var (response, clips) = ui.TryPerformAction(action);
        return new PlayerMoveResult(response, clips);
    }

    public ValueTask DisposeAsync()
    {
        session.Dispose();
        return ValueTask.CompletedTask;
    }
}
