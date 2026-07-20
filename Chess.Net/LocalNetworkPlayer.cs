using System.Collections.Immutable;
using Chess.Lib.UI;
using Chess.UCI;
using DIR.Lib;

namespace Chess.Net;

/// <summary>
/// The local player in a LAN game: plays exactly as the wrapped human normally would, but relays each
/// move the human commits to the peer over the <see cref="NetworkSession"/>. Assigned to the local
/// colour in <c>GameLoop</c> (the remote colour gets <see cref="NetworkPlayer"/>). A ply added by our
/// own <see cref="TryMakeMove"/> call is, by construction, the human's move — the peer's moves are
/// applied on the NetworkPlayer's turns, never here — so that's exactly what we relay.
/// </summary>
public sealed class LocalNetworkPlayer(IGamePlayer inner, NetworkSession session) : IGamePlayer
{
    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        if (session.PeerLeft)
            return new PlayerMoveResult(UIResponse.NeedsRestart, ImmutableArray<RectInt>.Empty);

        var pliesBefore = ui.Game.Plies.Count;
        var result = inner.TryMakeMove(ui);

        var plies = ui.Game.Plies;
        if (plies.Count == pliesBefore + 1)
        {
            session.SendMove(UciMove.FormatPly(plies[plies.Count - 1]));
        }

        return result;
    }
}
