using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Chess.Net.Cloud;

/// <summary>
/// One cloud game as an <see cref="ISessionConnection"/>: the database row is the channel, and the
/// moves in it arrive as <see cref="SessionProtocol"/> lines. That is what lets
/// <see cref="NetworkSession"/>, <c>NetworkPlayer</c> and the hosts' drain loops serve a cloud game
/// without a line of change — the courier swaps underneath them.
///
/// <para><b>The asymmetry with the browser is deliberate, not drift.</b> Chess.Web reads the row
/// directly (<c>CloudCourier</c>) because a cloud game is one shared STATE that both sides extend, and
/// it has no <c>Chess.Net</c> to reuse. The native side has one, and everything in it — the session,
/// the players, Android's <c>DrainNetworkMoves</c> — is written against a move stream. So the shared
/// state stays the wire format, where the rules can police it, and this class is the ~100 lines that
/// present it locally as the stream every caller here already expects. Nothing is duplicated: the
/// schema, the append-only rule and the turn gate remain the single authority.</para>
///
/// <para><b>Resume falls out of it.</b> A row carries the whole game, not the plies since you last
/// looked, so a connection opened on a game already in progress replays every ply as a MOVE line and
/// the host rebuilds the position from an empty board — the same thing a pasted link does. Pass the
/// moves you already have as <c>known</c> to start mid-game instead.</para>
///
/// <para><b>What the schema has no room for is RESIGN.</b> A row is <c>{g, n, w, b}</c> and unknown
/// fields are rejected, so leaving cannot be announced; a correspondence opponent is not present to
/// be told anyway. <see cref="Send"/> therefore drops it rather than pretending, and
/// <see cref="Closed"/> means our own subscription ended or the row stopped being ours — never "the
/// peer left".</para>
/// </summary>
public sealed class CloudGameConnection : ISessionConnection
{
    private readonly ICloudDatabase _db;
    private readonly string _gameId;
    private readonly object _lock = new();

    // The move log we have already accounted for: everything we have surfaced to the caller plus
    // everything we have sent. The subscription echoes our own appends back, and this is what tells
    // one of those apart from the opponent's move.
    private string _accounted;

    private IDisposable? _watch;
    private volatile bool _closed;

    public CloudGameConnection(ICloudDatabase db, string gameId, string known = "")
    {
        _db = db;
        _gameId = gameId;
        _accounted = known;
    }

    /// <summary>The database path this game lives at.</summary>
    public string Path => $"games/{_gameId}";

    public bool IsConnected => !_closed;

    public event Action<string>? LineReceived;
    public event Action? Closed;

    /// <summary>
    /// Subscribing here rather than in the constructor is what makes the "hold lines until the
    /// handlers are wired" contract unnecessary on this side: unlike a socket, a subscription
    /// delivers nothing until it is opened, so there is no window in which the first ply could
    /// arrive unheard. Idempotent.
    /// </summary>
    public void StartReceiving()
    {
        lock (_lock)
        {
            if (_watch is not null || _closed) return;
            _watch = _db.Watch(Path, OnRow);
        }
    }

    public void Send(string line)
    {
        var msg = SessionProtocol.Parse(line);
        if (msg.Kind == SessionMessageKind.Move)
            _ = AppendAsync(msg.Move);
        // Every other verb belongs to a handshake that happened before this channel existed (INVITE /
        // ACCEPT / DECLINE are the lobby's, over rows of their own), or has nowhere to go (RESIGN).
    }

    private async Task AppendAsync(string uci)
    {
        string before, after;
        int n;
        lock (_lock)
        {
            before = _accounted;
            after = before.Length == 0 ? uci : $"{before}.{uci}";
            n = PlyCount(after);
            // Account for the ply BEFORE writing it. Our own append comes back through the
            // subscription, and whether that echo beats this call's own response is not ours to
            // decide — so the only safe order is to have already claimed it.
            _accounted = after;
        }

        var json = $"{{\"g\":{CloudJson.Quote(after)},\"n\":{n}}}";
        if (await _db.UpdateAsync(Path, json).ConfigureAwait(false))
            return;

        // Refused: not our turn, or the opponent's ply landed first. Give the claim back, so the next
        // attempt extends what the server actually holds rather than a ply it never took. The
        // subscription is about to deliver whatever really happened.
        lock (_lock)
        {
            if (_accounted == after) _accounted = before;
        }
    }

    private void OnRow(string? json)
    {
        if (json is null)
        {
            // The row is gone, or has stopped being ours to read. Either way there is no game here
            // any more and the host should unwind, which is what Closed means to NetworkSession.
            Close();
            return;
        }

        using var doc = CloudJson.TryParse(json);
        if (doc is null) return;

        var moves = CloudJson.StringOr(doc.RootElement, "g");
        List<string> fresh = [];

        lock (_lock)
        {
            if (moves == _accounted) return; // our own append, echoed back

            if (!moves.StartsWith(_accounted, StringComparison.Ordinal))
            {
                // The stored history is not an extension of ours. The append-only rule makes this
                // impossible from a well-behaved server, so continuing would mean playing on a board
                // that no longer matches the one of record — close instead of guessing.
                Close();
                return;
            }

            var tail = moves[_accounted.Length..].TrimStart('.');
            if (tail.Length > 0)
                fresh.AddRange(tail.Split('.', StringSplitOptions.RemoveEmptyEntries));
            _accounted = moves;
        }

        foreach (var uci in fresh)
            LineReceived?.Invoke(SessionProtocol.EncodeMove(uci));
    }

    private static int PlyCount(string moves) =>
        moves.Length == 0 ? 0 : moves.Split('.', StringSplitOptions.RemoveEmptyEntries).Length;

    private void Close()
    {
        IDisposable? watch;
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            watch = _watch;
            _watch = null;
        }
        watch?.Dispose();
        Closed?.Invoke();
    }

    public void Dispose() => Close();
}
