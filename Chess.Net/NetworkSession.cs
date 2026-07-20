using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Chess.Lib;

namespace Chess.Net;

/// <summary>
/// An established game connection between two peers: the TCP link that carried the invite now carries
/// the move stream. Incoming moves land on a background thread and are parked in a thread-safe queue;
/// the game thread drains them via <see cref="TryDequeueMove"/> — the same hand-off shape as
/// <c>Chess.GUI.HumanPlayer</c>'s input queue, keeping the socket off the single-threaded GameUI.
/// </summary>
public sealed class NetworkSession : IDisposable
{
    private readonly ILanConnection _conn;
    private readonly ConcurrentQueue<string> _incomingMoves = new();
    private volatile bool _peerLeft;

    /// <summary>The colour THIS end plays.</summary>
    public Side LocalSide { get; }

    /// <summary>The colour the remote peer plays.</summary>
    public Side RemoteSide => LocalSide == Side.White ? Side.Black : Side.White;

    /// <summary>The peer's display name (for status/chrome).</summary>
    public string PeerName { get; }

    /// <summary>True once the peer resigned or the socket dropped — the game should unwind to menu.</summary>
    public bool PeerLeft => _peerLeft;

    /// <summary>True when the peer has sent a move we haven't drained yet. Lets a host with no game
    /// loop (Android) poll for redraws instead of blocking on a socket.</summary>
    public bool HasIncomingMove => !_incomingMoves.IsEmpty;

    public NetworkSession(ILanConnection conn, Side localSide, string peerName)
    {
        _conn = conn;
        LocalSide = localSide;
        PeerName = peerName;
        _conn.LineReceived += OnLine;
        _conn.Closed += OnClosed;
        // A close that fired between accept and here (peer bailed instantly) still gets caught.
        if (!_conn.IsConnected) _peerLeft = true;
    }

    /// <summary>Relay a move we just made to the peer.</summary>
    public void SendMove(string uci) => _conn.Send(LanProtocol.EncodeMove(uci));

    /// <summary>Pop the next move the peer sent, if any.</summary>
    public bool TryDequeueMove([MaybeNullWhen(false)] out string uci) => _incomingMoves.TryDequeue(out uci);

    private void OnLine(string line)
    {
        var msg = LanProtocol.Parse(line);
        switch (msg.Kind)
        {
            case LanMessageKind.Move:
                _incomingMoves.Enqueue(msg.Move);
                break;
            case LanMessageKind.Resign:
                _peerLeft = true;
                break;
        }
    }

    private void OnClosed() => _peerLeft = true;

    public void Dispose()
    {
        _conn.LineReceived -= OnLine;
        _conn.Closed -= OnClosed;
        // Closing our socket lets the peer's read loop hit EOF and see us leave (its OnClosed).
        _conn.Dispose();
    }
}
