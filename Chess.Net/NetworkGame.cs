using System;
using Chess.Lib;
using Chess.Lib.UI;

namespace Chess.Net;

/// <summary>
/// The one place the LAN-game wiring lives, so every front-end (desktop, console, and any future one)
/// stops re-typing the same load-bearing invariants: the remote peer occupies the engine-shaped slot
/// of <see cref="GameLoop"/> as a <see cref="NetworkPlayer"/> playing <see cref="NetworkSession.RemoteSide"/>,
/// the local human is wrapped in a <see cref="LocalNetworkPlayer"/> so each committed move is relayed,
/// and a LAN game always begins from a fresh board with White to move (it is never resumed).
/// </summary>
public static class NetworkGame
{
    /// <summary>
    /// Builds the <see cref="GameLoop"/> for a connected <paramref name="session"/> plus the two side
    /// arguments to hand straight to <see cref="GameLoop.RunAsync"/> with <see cref="GameMode.NetworkGame"/>.
    /// </summary>
    /// <param name="localHumanFactory">Produces the local human player the loop drives — wrapped in a
    /// <see cref="LocalNetworkPlayer"/> here so its moves are sent to the peer. (Heads differ: the GUI
    /// reuses one instance, the console makes a fresh one, so this is a factory, not an instance.)</param>
    /// <returns>The wired loop and the <c>ComputerSide</c>/<c>SideToMove</c> for <c>RunAsync</c>.</returns>
    public static (GameLoop Loop, Side ComputerSide, Side SideToMove) CreateLoop(
        TimeProvider timeProvider,
        Func<IGameDisplay> displayFactory,
        Func<IGamePlayer> localHumanFactory,
        NetworkSession session)
    {
        var loop = new GameLoop(
            timeProvider,
            displayFactory,
            () => new LocalNetworkPlayer(localHumanFactory(), session),
            (_, _) => new NetworkPlayer(session));

        return (loop, session.RemoteSide, Side.White);
    }
}
