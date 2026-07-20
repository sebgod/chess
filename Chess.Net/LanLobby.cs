using System;
using System.Collections.Generic;
using Chess.Lib;

namespace Chess.Net;

/// <summary>Where the lobby is in the invite dance. Front-ends poll <see cref="LanLobby.State"/> each
/// frame and render accordingly.</summary>
public enum LobbyState
{
    Browsing,        // showing the peer list; can invite or receive an invite
    Inviting,        // we dialed a peer and are waiting for Accept/Decline
    IncomingInvite,  // a peer invited us; awaiting our Accept/Decline (see Incoming)
    Connecting,      // (reserved) handshake in flight
    Connected,       // Session is ready — start the game
    Declined,        // our invite was declined
    Cancelled,       // we backed out
    Failed,          // couldn't reach the peer / it went away
}

/// <summary>Details of an invite we've received, surfaced while <see cref="LanLobby.State"/> is
/// <see cref="LobbyState.IncomingInvite"/>.</summary>
public sealed record IncomingInvite(string PeerName, Side YourSide);

/// <summary>
/// Orchestrates discovery + the invite/accept handshake into a ready <see cref="NetworkSession"/>.
/// Kept separate from <c>StartupWizard</c> on purpose: the lobby is live and asynchronous (a peer
/// list that updates, invites that arrive unprompted), which the wizard's synchronous
/// <c>Confirm(int)</c> model can't express — the wizard only routes the user *into* this.
///
/// <para>Colour rule: the inviter's chosen colour stands; the invitee plays the opposite. So when we
/// invite we use our <paramref name="preferredColor"/>; when we're invited we ignore it and take the
/// opposite of what the invite carried (surfaced in <see cref="Incoming"/> as YourSide).</para>
///
/// <para>State fields are written from background socket callbacks and read from the UI poll, so they
/// are volatile; multi-field transitions take <c>_lock</c> so an inbound invite and an outbound one
/// can't interleave.</para>
/// </summary>
public sealed class LanLobby : IDisposable
{
    private readonly ILanTransport _transport;
    private readonly LanDiscovery _discovery;
    private readonly LanIdentity _identity;
    private readonly Side _preferredColor;
    private readonly object _lock = new();

    private ILanConnection? _pending;
    private string _pendingPeerName = "";
    private Side _pendingLocalSide;

    private volatile LobbyState _state = LobbyState.Browsing;
    private volatile IncomingInvite? _incoming;
    private volatile NetworkSession? _session;
    private volatile string? _statusMessage;

    public LanLobby(ILanTransport transport, LanDiscovery discovery, LanIdentity identity, Side preferredColor)
    {
        _transport = transport;
        _discovery = discovery;
        _identity = identity;
        _preferredColor = preferredColor == Side.None ? Side.White : preferredColor;
        _transport.ConnectionAccepted += OnInboundConnection;
    }

    public LobbyState State => _state;
    public IncomingInvite? Incoming => _incoming;
    public NetworkSession? Session => _session;
    public string? StatusMessage => _statusMessage;
    public string LocalName => _identity.Name;
    public IReadOnlyList<LanPeer> Peers => _discovery.Peers;

    /// <summary>Begin announcing/listening.</summary>
    public void Start() => _discovery.Start();

    /// <summary>Invite a discovered peer to play (we become the inviter; our colour stands).</summary>
    public async void Invite(LanPeer peer)
    {
        lock (_lock)
        {
            if (_state != LobbyState.Browsing) return;
            _state = LobbyState.Inviting;
            _statusMessage = $"Inviting {peer.DisplayName}…";
            _pendingPeerName = peer.DisplayName;
            _pendingLocalSide = _preferredColor;
        }

        try
        {
            var conn = await _transport.ConnectAsync(peer.EndPoint);
            lock (_lock)
            {
                if (_state != LobbyState.Inviting) { conn.Dispose(); return; } // cancelled while dialing
                _pending = conn;
                conn.LineReceived += OnInviteReply;
                conn.Closed += OnPendingClosed;
            }
            conn.Send(LanProtocol.EncodeInvite(_identity.PeerId, _identity.Name, _pendingLocalSide));
        }
        catch
        {
            Fail($"Couldn't reach {peer.DisplayName}");
        }
    }

    /// <summary>Accept the invite currently in <see cref="Incoming"/>.</summary>
    public void Accept()
    {
        ILanConnection conn;
        lock (_lock)
        {
            if (_state != LobbyState.IncomingInvite || _pending is null) return;
            conn = _pending;
            conn.LineReceived -= OnInboundLine;
            conn.Closed -= OnPendingClosed;
            // Build the session (which subscribes to the connection) BEFORE sending ACCEPT so the
            // first move the peer sends back (they may move first) can't slip through a gap.
            _session = new NetworkSession(conn, _pendingLocalSide, _pendingPeerName);
            _pending = null;
            _incoming = null;
            _state = LobbyState.Connected;
        }
        // Outside the lock: sending can re-enter our callbacks (a synchronous transport may echo a
        // close back), and _pending is already cleared, so nothing here can be corrupted.
        conn.Send(LanProtocol.EncodeAccept());
    }

    /// <summary>Decline the invite currently in <see cref="Incoming"/>.</summary>
    public void Decline()
    {
        ILanConnection conn;
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
        try { conn.Send(LanProtocol.EncodeDecline()); } catch { }
        conn.Dispose();
    }

    /// <summary>Back out of an in-flight invite (either direction) and return to browsing.</summary>
    public void Cancel()
    {
        ILanConnection? conn;
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

    private void OnInboundConnection(ILanConnection conn)
    {
        lock (_lock)
        {
            // Entertain one invite at a time; if we're mid-anything, politely refuse.
            if (_state != LobbyState.Browsing)
            {
                try { conn.Send(LanProtocol.EncodeDecline()); } catch { }
                conn.Dispose();
                return;
            }
            _pending = conn;
            conn.LineReceived += OnInboundLine;
            conn.Closed += OnPendingClosed;
        }
    }

    private void OnInboundLine(string line)
    {
        var msg = LanProtocol.Parse(line);
        if (msg.Kind != LanMessageKind.Invite) return;

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
        var msg = LanProtocol.Parse(line);
        lock (_lock)
        {
            if (_state != LobbyState.Inviting || _pending is null) return;
            var conn = _pending;
            if (msg.Kind == LanMessageKind.Accept)
            {
                conn.LineReceived -= OnInviteReply;
                conn.Closed -= OnPendingClosed;
                _session = new NetworkSession(conn, _pendingLocalSide, _pendingPeerName);
                _pending = null;
                _state = LobbyState.Connected;
            }
            else if (msg.Kind == LanMessageKind.Decline)
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

    public void Dispose()
    {
        _transport.ConnectionAccepted -= OnInboundConnection;
        try { _discovery.SendBye(); } catch { }
        // If we never connected, drop the pending connection; once Connected the socket is owned by
        // the NetworkSession and must survive this Dispose.
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
