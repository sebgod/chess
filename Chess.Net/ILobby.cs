using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chess.Lib;

namespace Chess.Net;

/// <summary>Where the lobby is in the invite dance. Front-ends poll <see cref="ILobby.State"/> each
/// frame and render accordingly.</summary>
public enum LobbyState
{
    Browsing,        // showing the peer list; can invite or receive an invite
    Inviting,        // we dialed a peer and are waiting for Accept/Decline
    IncomingInvite,  // a peer invited us; awaiting our Accept/Decline (see Incoming)
    Connecting,      // we accepted; ACCEPT is going out (see Accept) — Session is not published yet
    Connected,       // Session is ready — start the game
    Declined,        // our invite was declined
    Failed,          // couldn't reach the peer / it went away
}

/// <summary>Details of an invite we've received, surfaced while <see cref="ILobby.State"/> is
/// <see cref="LobbyState.IncomingInvite"/>.</summary>
public sealed record IncomingInvite(string PeerName, Side YourSide);

/// <summary>
/// Someone we could invite, as a lobby screen needs them: a <see cref="Label"/> to show and an
/// <see cref="Id"/> to name them by when the user picks one. Nothing else — deliberately.
///
/// <para>The label is already <b>disambiguated against the rest of the list</b> (LAN's is
/// <c>LanPeer.ResolveLabels</c>, which turns two "Seb"s into "Seb (lap1)" and "Seb (lap1) #2"), so a
/// front-end renders <c>Peers</c> straight into menu items. All three used to run that themselves
/// and then zip the labels back to peers by index; doing it once here is what lets a second courier
/// reuse the same three screens.</para>
///
/// <para><see cref="Id"/> is the courier's own handle for the peer — a LAN <c>PeerId</c>, a cloud
/// uid — and is opaque to the caller: <see cref="ILobby.Invite"/> resolves it back. That indirection
/// is not ceremony. A peer list is a live table, and the peer a user tapped may have expired between
/// the frame that drew it and the tap; resolving at invite time turns that into "X went away"
/// instead of a dial to an address nobody is listening on.</para>
/// </summary>
public sealed record LobbyPeer(string Id, string Label);

/// <summary>
/// A lobby: who is out there, and the invite handshake that turns one of them into a
/// <see cref="NetworkSession"/>. One interface over both couriers — <see cref="LanLobby"/> (UDP
/// beacon + TCP invite) and, later, the cloud (an <c>/open</c> table + an invite row) — so the three
/// front-end lobby screens are written once.
///
/// <para>The couriers differ in <b>discovery</b>, not in handshake, which is why this is one
/// interface and not two: a LAN peer's beacon is a standing "I am here and playable", and the
/// cloud's <c>/open/{uid}</c> is that beacon persisted. Every <see cref="LobbyState"/> member has an
/// exact counterpart on both sides — <see cref="Accept"/> is "reply ACCEPT" on the LAN and "claim the
/// seat the inviter left empty" in the cloud, which the deployed database rules already gate.</para>
///
/// <para>Where they do differ is <b>policy over time</b>, and that difference lives in the
/// implementations rather than here: LAN's <see cref="LobbyState.Inviting"/> lives in a socket, so an
/// unanswered invite means the peer walked away and timing out is correct; a cloud invite is a row
/// that outlives the connection, and one answered four hours later is correspondence play working as
/// intended.</para>
///
/// <para>Threading: state is written from background callbacks (a socket read, an event stream) and
/// read from a UI poll, so every getter here is safe to read from the render thread at any time and
/// none of them block.</para>
/// </summary>
public interface ILobby : IAsyncDisposable
{
    /// <summary>Drives what the lobby screen shows; poll it every frame.</summary>
    LobbyState State { get; }

    /// <summary>The invite awaiting our answer while <see cref="State"/> is
    /// <see cref="LobbyState.IncomingInvite"/>.</summary>
    IncomingInvite? Incoming { get; }

    /// <summary>The connected session, once <see cref="State"/> reaches
    /// <see cref="LobbyState.Connected"/>. The host takes it and starts the game; its connection
    /// outlives this lobby.</summary>
    NetworkSession? Session { get; }

    /// <summary>A line to show the user: who we are inviting, who declined, what went wrong.</summary>
    string? StatusMessage { get; }

    /// <summary>The name we are announcing ourselves under.</summary>
    string LocalName { get; }

    /// <summary>Who we could invite, labelled for display. Live — re-read it each frame.</summary>
    IReadOnlyList<LobbyPeer> Peers { get; }

    /// <summary>Begin announcing ourselves and listening for invites.</summary>
    void Start();

    /// <summary>Invite one of <see cref="Peers"/> (we become the inviter, so our colour stands and
    /// the invitee takes the opposite).</summary>
    void Invite(LobbyPeer peer);

    /// <summary>Accept the invite in <see cref="Incoming"/>.</summary>
    void Accept();

    /// <summary>Decline the invite in <see cref="Incoming"/>.</summary>
    void Decline();

    /// <summary>Back out of an in-flight invite (either direction), or clear a
    /// <see cref="LobbyState.Declined"/>/<see cref="LobbyState.Failed"/> message, and return to
    /// browsing.</summary>
    void Cancel();
}
