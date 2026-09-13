using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Chess.Lib;
using LAN.Lib;

namespace Chess.Net;

/// <summary>
/// The LAN implementation of <see cref="ILobby"/>: orchestrates discovery + the invite/accept
/// handshake into a ready <see cref="NetworkSession"/>.
/// Kept separate from <c>StartupWizard</c> on purpose: the lobby is live and asynchronous (a peer
/// list that updates, invites that arrive unprompted), which the wizard's synchronous
/// <c>Confirm(int)</c> model can't express — the wizard only routes the user *into* this.
///
/// <para>Two layers meet here: <c>LAN.Lib</c>'s <see cref="LanDiscovery"/> says who is out there
/// (UDP beacon + peer table, shared with every SharpAstro app, hence the
/// <see cref="SessionProtocol.ServiceName"/> filter on <see cref="Peers"/>), and chess's own
/// <see cref="ISessionTransport"/> opens the TCP channel to one of them.</para>
///
/// <para>Colour rule: the inviter's chosen colour stands; the invitee plays the opposite. So when we
/// invite we use our <paramref name="preferredColor"/>; when we're invited we ignore it and take the
/// opposite of what the invite carried (surfaced in <see cref="Incoming"/> as YourSide).</para>
///
/// <para>State fields are written from background socket callbacks and read from the UI poll, so they
/// are volatile; multi-field transitions take <c>_lock</c> so an inbound invite and an outbound one
/// can't interleave.</para>
/// </summary>
public sealed class LanLobby : ILobby
{
    private readonly ISessionTransport _transport;
    private readonly LanDiscovery _discovery;
    private readonly LanIdentity _identity;
    private readonly string _localName;
    private readonly Side _preferredColor;
    private readonly object _lock = new();

    private ISessionConnection? _pending;
    private string _pendingPeerName = "";
    private Side _pendingLocalSide;

    private volatile LobbyState _state = LobbyState.Browsing;
    private volatile IncomingInvite? _incoming;
    private volatile NetworkSession? _session;
    private volatile string? _statusMessage;

    public LanLobby(ISessionTransport transport, LanDiscovery discovery, LanIdentity identity,
        string localName, Side preferredColor)
    {
        _transport = transport;
        _discovery = discovery;
        _identity = identity;
        _localName = localName;
        _preferredColor = preferredColor == Side.None ? Side.White : preferredColor;
        _transport.ConnectionAccepted += OnInboundConnection;
    }

    public LobbyState State => _state;
    public IncomingInvite? Incoming => _incoming;
    public NetworkSession? Session => _session;
    public string? StatusMessage => _statusMessage;
    public string LocalName => _localName;

    /// <summary>The chess players on the LAN, labelled for a menu. Filtered by service: the discovery
    /// port is shared with every other SharpAstro app, so an unfiltered table would list telescope
    /// rigs as opponents.</summary>
    public IReadOnlyList<LobbyPeer> Peers
    {
        get
        {
            // ResolveLabels belongs here rather than in each front-end: it needs the WHOLE list to
            // decide how far to disambiguate, and it answers positionally, so pairing each label back
            // with its peer is precisely the step that was repeated in all three lobby screens.
            var peers = _discovery.PeersOf(SessionProtocol.ServiceName);
            var labels = LanPeer.ResolveLabels(peers);
            var result = new LobbyPeer[peers.Count];
            for (var i = 0; i < peers.Count; i++)
                result[i] = new LobbyPeer(peers[i].PeerId, labels[i]);
            return result;
        }
    }

    /// <summary>Begin announcing/listening. Fire-and-forget: the first beacon is a UDP send whose
    /// failures the transport already swallows (as does the timer that repeats it), so there is
    /// nothing here for a caller to await or observe.</summary>
    public void Start() => _ = _discovery.StartAsync();

    /// <summary>Invite a discovered peer to play (we become the inviter; our colour stands).</summary>
    public void Invite(LobbyPeer peer)
    {
        lock (_lock)
        {
            if (_state != LobbyState.Browsing) return;
            _state = LobbyState.Inviting;
            _statusMessage = $"Inviting {peer.Label}…";
            _pendingPeerName = peer.Label;
            _pendingLocalSide = _preferredColor;
        }

        // Resolve the id back to a live peer only now. The list the user picked from was drawn a frame
        // ago, and a peer that has stopped beaconing since is already out of the table — which this
        // reports as "went away" instead of dialing an address nobody is listening on any more.
        LanPeer? target = null;
        foreach (var candidate in _discovery.PeersOf(SessionProtocol.ServiceName))
        {
            if (candidate.PeerId == peer.Id) { target = candidate; break; }
        }

        if (target is null)
        {
            Fail($"{peer.Label} went away");
            return;
        }

        // Dialing is asynchronous but nothing awaits the lobby: the result lands in _state, which the
        // caller is polling anyway. The body catches everything, so the discarded task can't carry an
        // exception away unobserved (which is the whole reason this is not `async void`).
        _ = DialAsync(target.EndPoint, peer.Label);
    }

    private async Task DialAsync(IPEndPoint endPoint, string label)
    {
        try
        {
            var conn = await _transport.ConnectAsync(endPoint);
            lock (_lock)
            {
                if (_state != LobbyState.Inviting) { conn.Dispose(); return; } // cancelled while dialing
                _pending = conn;
                conn.LineReceived += OnInviteReply;
                conn.Closed += OnPendingClosed;
            }
            conn.StartReceiving(); // handlers are wired — safe to deliver (see ISessionConnection)
            conn.Send(SessionProtocol.EncodeInvite(_identity.PeerId, _localName, _pendingLocalSide));
        }
        catch
        {
            Fail($"Couldn't reach {label}");
        }
    }

    /// <summary>
    /// Accept the invite currently in <see cref="Incoming"/>.
    ///
    /// <para>The handshake is published in two steps, and the order is the point. <c>Connected</c> is
    /// the host's cue to start the game, and if we drew White (the inviter chose Black) the very next
    /// thing that host may do is send a move. So <c>Connected</c> — and with it <see cref="Session"/> —
    /// must not become visible until ACCEPT is actually on the wire: the inviter is still
    /// <c>Inviting</c> until it reads ACCEPT, and <see cref="OnInviteReply"/> drops every line that
    /// isn't ACCEPT/DECLINE, so a move that overtook the ACCEPT would be swallowed and both ends would
    /// sit waiting for each other. <see cref="LobbyState.Connecting"/> covers that window.</para>
    /// </summary>
    public void Accept()
    {
        ISessionConnection conn;
        NetworkSession session;
        lock (_lock)
        {
            if (_state != LobbyState.IncomingInvite || _pending is null) return;
            conn = _pending;
            conn.LineReceived -= OnInboundLine;
            conn.Closed -= OnPendingClosed;
            // Build the session (which subscribes to the connection) BEFORE sending ACCEPT so the
            // first move the peer sends back (they may move first) can't slip through a gap. It stays
            // a local until the send returns — see the two-step note above.
            session = new NetworkSession(conn, _pendingLocalSide, _pendingPeerName);
            _pending = null;
            _incoming = null;
            _state = LobbyState.Connecting;
        }

        // Outside the lock: sending can re-enter our callbacks (a synchronous transport may echo a
        // close back), and _pending is already cleared, so nothing here can be corrupted.
        conn.Send(SessionProtocol.EncodeAccept());

        lock (_lock)
        {
            // Still ours to finish? A Cancel(), or a close cascading out of the send, may have moved
            // the lobby on while we were sending — then the session we built is orphaned.
            if (_state == LobbyState.Connecting && !session.PeerLeft)
            {
                _session = session;
                _state = LobbyState.Connected;
                return;
            }
            if (_state == LobbyState.Connecting)
            {
                // The peer vanished mid-handshake: stay in the lobby and say so, rather than handing
                // the host a session that is already dead.
                _statusMessage = $"{_pendingPeerName} went away";
                _state = LobbyState.Failed;
            }
        }
        session.Dispose(); // outside the lock: disposing closes the socket, which re-enters callbacks
    }

    /// <summary>Decline the invite currently in <see cref="Incoming"/>.</summary>
    public void Decline()
    {
        ISessionConnection conn;
        lock (_lock)
        {
            if (_state != LobbyState.IncomingInvite || _pending is null) return;
            conn = _pending;
            conn.LineReceived -= OnInboundLine;
            conn.Closed -= OnPendingClosed;
            _pending = null;
            _incoming = null;
            _state = LobbyState.Browsing;
        }
        // Send + dispose on the captured local, after the field is cleared — a close cascade from the
        // dispose can't null a field we still need (the bug this ordering fixes).
        try { conn.Send(SessionProtocol.EncodeDecline()); } catch { }
        conn.Dispose();
    }

    /// <summary>Back out of an in-flight invite (either direction) and return to browsing.</summary>
    public void Cancel()
    {
        ISessionConnection? conn;
        lock (_lock)
        {
            if (_state == LobbyState.Connected) return;
            conn = _pending;
            if (conn is not null)
            {
                conn.LineReceived -= OnInviteReply;
                conn.LineReceived -= OnInboundLine;
                conn.Closed -= OnPendingClosed;
                _pending = null;
            }
            _incoming = null;
            _state = LobbyState.Browsing;
            _statusMessage = null;
        }
        conn?.Dispose();
    }

    private void OnInboundConnection(ISessionConnection conn)
    {
        lock (_lock)
        {
            // Entertain one invite at a time; if we're mid-anything, politely refuse.
            if (_state != LobbyState.Browsing)
            {
                try { conn.Send(SessionProtocol.EncodeDecline()); } catch { }
                conn.Dispose();
                return;
            }
            _pending = conn;
            conn.LineReceived += OnInboundLine;
            conn.Closed += OnPendingClosed;
        }
        // Outside the lock: the peer's INVITE is already sitting in the socket buffer, so this can
        // deliver it (and re-enter our handler) the moment it is called.
        conn.StartReceiving();
    }

    private void OnInboundLine(string line)
    {
        var msg = SessionProtocol.Parse(line);
        if (msg.Kind != SessionMessageKind.Invite) return;

        lock (_lock)
        {
            if (_state != LobbyState.Browsing) return;
            _pendingPeerName = string.IsNullOrWhiteSpace(msg.Name) ? "Player" : msg.Name;
            var inviterColor = msg.Color == Side.None ? Side.White : msg.Color;
            _pendingLocalSide = inviterColor == Side.White ? Side.Black : Side.White; // opposite of inviter
            _incoming = new IncomingInvite(_pendingPeerName, _pendingLocalSide);
            _state = LobbyState.IncomingInvite;
        }
    }

    private void OnInviteReply(string line)
    {
        var msg = SessionProtocol.Parse(line);
        lock (_lock)
        {
            if (_state != LobbyState.Inviting || _pending is null) return;
            var conn = _pending;
            if (msg.Kind == SessionMessageKind.Accept)
            {
                conn.LineReceived -= OnInviteReply;
                conn.Closed -= OnPendingClosed;
                _session = new NetworkSession(conn, _pendingLocalSide, _pendingPeerName);
                _pending = null;
                _state = LobbyState.Connected;
            }
            else if (msg.Kind == SessionMessageKind.Decline)
            {
                conn.LineReceived -= OnInviteReply;
                conn.Closed -= OnPendingClosed;
                conn.Dispose();
                _pending = null;
                _statusMessage = $"{_pendingPeerName} declined";
                _state = LobbyState.Declined;
            }
        }
    }

    private void OnPendingClosed()
    {
        lock (_lock)
        {
            if (_state is LobbyState.Inviting or LobbyState.IncomingInvite)
            {
                _pending = null;
                _incoming = null;
                _statusMessage = $"{_pendingPeerName} went away";
                _state = LobbyState.Failed;
            }
        }
    }

    private void Fail(string message)
    {
        lock (_lock)
        {
            if (_pending is not null) { _pending.Dispose(); _pending = null; }
            _statusMessage = message;
            _state = LobbyState.Failed;
        }
    }

    /// <summary>
    /// Leaves the lobby. Asynchronous because saying goodbye is: the bye is a real UDP send, and
    /// peers only drop us promptly if it actually goes out (expiry is the 5-second fallback).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _transport.ConnectionAccepted -= OnInboundConnection;
        try { await _discovery.SendByeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        // If we never connected, drop the pending connection; once Connected the socket is owned by
        // the NetworkSession and must survive this teardown.
        lock (_lock)
        {
            if (_state != LobbyState.Connected && _pending is not null)
            {
                _pending.Dispose();
                _pending = null;
            }
        }
        _discovery.Dispose();
    }
}
