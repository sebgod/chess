using System;
using System.Threading.Tasks;
using Chess.Lib;
using LAN.Lib;

namespace Chess.Net;

/// <summary>
/// The whole LAN-play stack for one visit to the lobby, opened and torn down as a unit: LAN.Lib's UDP
/// discovery (announcing us as service <see cref="SessionProtocol.ServiceName"/> at our TCP port, and
/// listing the other chess players), chess's own TCP <see cref="ISessionTransport"/>, and the
/// <see cref="LanLobby"/> that marries the two. Every front-end (console, desktop GUI, Android) opens
/// it the same way instead of hand-wiring four objects in the right order — and, more to the point,
/// none of them can forget the service name that keeps a telescope rig out of the chess lobby.
///
/// <para>Disposing closes the sockets discovery and the invite handshake used — but NOT an established
/// <see cref="NetworkSession"/>: its TCP connection is its own, so the lobby that produced a session is
/// torn down while the game plays on (which is what every host does).</para>
/// </summary>
public sealed class LanPlayStack : IAsyncDisposable
{
    private readonly UdpLanTransport _udp;
    private readonly TcpSessionTransport _tcp;
    private readonly LanDiscovery _discovery;

    /// <param name="localName">The display name we announce; other players pick it from their lobby.</param>
    /// <param name="preferredColor">Our colour when WE invite (the invitee takes the other).</param>
    /// <param name="time">Drives beacon cadence and peer expiry — <c>TimeProvider.System</c> in production.</param>
    public LanPlayStack(string localName, Side preferredColor, TimeProvider time)
    {
        _udp = new UdpLanTransport();
        _tcp = new TcpSessionTransport();

        var options = new LanDiscoveryOptions
        {
            ServiceName = SessionProtocol.ServiceName,
            ServicePort = _tcp.ListenPort, // where a peer dials us to invite
            NodeName = localName,
            // No StableNodeIdPath: a chess peer is only ever bound to for the length of one lobby
            // visit, so there is nothing worth recognising across restarts (unlike a tianwen rig).
        };

        // One identity for both halves: the beacon filters our own echo by its PeerId, and the invite
        // we send over TCP carries the same id, so a peer sees one consistent player.
        var identity = LanIdentity.Create(stableNodeIdPath: null);
        _discovery = new LanDiscovery(_udp, time, options, identity);
        Lobby = new LanLobby(_tcp, _discovery, identity, localName, preferredColor);
    }

    /// <summary>The lobby to drive: <c>Start()</c>, poll <c>State</c>/<c>Peers</c>, then take
    /// <c>Session</c> once it reaches <see cref="LobbyState.Connected"/>.</summary>
    public LanLobby Lobby { get; }

    public async ValueTask DisposeAsync()
    {
        // Order matters: the lobby's bye has to go out over the UDP transport before we close it.
        await Lobby.DisposeAsync().ConfigureAwait(false);
        await _tcp.DisposeAsync().ConfigureAwait(false);
        await _udp.DisposeAsync().ConfigureAwait(false);
    }
}
