using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.UCI;

namespace Chess.Net.Cloud;

/// <summary>
/// The cloud implementation of <see cref="ILobby"/>: <c>/open</c> is the peer list, and taking
/// somebody's empty seat is the handshake. The three front-end lobby screens drive it through exactly
/// the calls they drive <see cref="LanLobby"/> through, which is what <see cref="ILobby"/> was drawn
/// for.
///
/// <para><b><see cref="Start"/> posts, because the beacon and the open row are the same idea.</b> On
/// the LAN, being visible is what <c>Start()</c> does — it announces you, and anyone may invite you.
/// <c>/open/{uid}</c> is that announcement persisted, so signing in, creating a game with our seat
/// taken and advertising it is this courier's announce. One row per player is a property of the path
/// rather than a count the rules could not enforce, and re-entering the lobby replaces it.</para>
///
/// <para><b>Claiming IS accepting.</b> The rules already gate a seat write with
/// <c>".write": "!data.exists()"</c> plus "the uid must be your own", so two players racing for one
/// seat are resolved server-side and the loser is told. That is the cloud's terminal transition, and
/// it was built and tested before this class existed — which is why shaping the cloud lobby like the
/// LAN one added a peer list, not a protocol.</para>
///
/// <para><b>Two states have no counterpart here and are simply unreachable:</b>
/// <see cref="LobbyState.IncomingInvite"/> and <see cref="LobbyState.Declined"/>. The deployed schema
/// has an <c>/invites</c> subtree for a directed invitation, rules and all, but nothing produces one
/// until there is a presence table separate from <c>/open</c> — somebody visible who has not posted.
/// Building the path before then would be plumbing with nothing flowing through it.</para>
///
/// <para><b>A claimed row can linger.</b> Only the poster may write <c>/open/{poster}</c>, so a
/// posting is withdrawn by the poster when they see their seat filled — and a poster who is offline
/// cannot. The row stays, someone tries to claim a game that is full, and the rules refuse it: the
/// lobby says the game was taken and the row disappears the next time its poster opens the app.
/// Self-healing, and the alternative is a rules change letting a claimer write someone else's
/// row.</para>
/// </summary>
public sealed class CloudLobby : ILobby
{
    /// <summary>
    /// How long a posting stays listed. Rows are durable on purpose — a posted correspondence game
    /// has to outlive the app that posted it, which is the whole point — so nothing on the server
    /// removes an abandoned one and the client hides it instead. Shorter than the browser's 120-day
    /// staleness for a game, because that measures a game somebody is still playing and this measures
    /// an invitation nobody took.
    /// </summary>
    private static readonly TimeSpan PostingStaysListed = TimeSpan.FromDays(7);

    private readonly ICloudDatabase _db;
    private readonly TimeProvider _time;
    private readonly string _localName;
    private readonly Side _preferredColor;
    private readonly object _lock = new();

    private IDisposable? _openWatch;
    private IDisposable? _mineWatch;
    private string _gameId = "";

    private volatile LobbyState _state = LobbyState.Browsing;
    private volatile NetworkSession? _session;
    private volatile string? _statusMessage;
    private volatile OpenGame[] _open = [];

    public CloudLobby(ICloudDatabase db, string localName, Side preferredColor, TimeProvider? time = null)
    {
        _db = db;
        _localName = localName;
        _preferredColor = preferredColor == Side.None ? Side.White : preferredColor;
        _time = time ?? TimeProvider.System;
    }

    public LobbyState State => _state;
    public NetworkSession? Session => _session;
    public string? StatusMessage => _statusMessage;
    public string LocalName => _localName;

    /// <summary>Always null: nobody can invite us directly (see the class summary).</summary>
    public IncomingInvite? Incoming => null;

    /// <summary>The games other people have open, newest first and our own left out — you cannot be
    /// your own opponent, and the rules would happily let you take both seats.</summary>
    public IReadOnlyList<LobbyPeer> Peers
    {
        get
        {
            // Staleness is judged HERE rather than when a row arrives, so a posting that ages out
            // while somebody sits in the lobby leaves it. Filtering on arrival would have meant the
            // list only ever changed when somebody else's did.
            var now = _time.GetUtcNow();
            return
            [
                .. _open.Where(row => !row.IsStale(now, PostingStaysListed))
                        .Select(row => new LobbyPeer(row.Host, row.Label))
            ];
        }
    }

    public void Start() => _ = StartAsync();

    private async Task StartAsync()
    {
        if (!await _db.SignInAsync().ConfigureAwait(false))
        {
            Fail("Couldn't reach the online service");
            return;
        }

        // Create the game first, then advertise it: the rules let anybody claim a seat in a game that
        // exists, and a posting pointing at a game that does not would be an invitation to nothing.
        var gameId = GameLinkCodec.NewCloudGameId();
        var seat = _preferredColor == Side.Black ? "b" : "w";
        var created = await _db.SetAsync($"games/{gameId}",
            $"{{\"g\":\"\",\"n\":0,\"{seat}\":{{\"uid\":{CloudJson.Quote(_db.Uid)}," +
            $"\"name\":{CloudJson.Quote(_localName)}}}}}").ConfigureAwait(false);

        var posted = created && await _db.SetAsync($"open/{_db.Uid}",
            $"{{\"gameId\":{CloudJson.Quote(gameId)},\"name\":{CloudJson.Quote(_localName)}," +
            $"\"color\":\"{seat}\",\"updated\":{{\".sv\":\"timestamp\"}}}}").ConfigureAwait(false);

        if (!posted)
        {
            Fail("Couldn't post a game");
            return;
        }

        lock (_lock)
        {
            if (_state != LobbyState.Browsing) return; // left again before this landed
            _gameId = gameId;
            _openWatch = _db.Watch("open", OnOpenRows);
            _mineWatch = _db.Watch($"games/{gameId}", OnOurGame);
        }
    }

    /// <summary>Take the empty seat in somebody's open game — the cloud's whole handshake.</summary>
    public void Invite(LobbyPeer peer)
    {
        OpenGame? row;
        lock (_lock)
        {
            if (_state != LobbyState.Browsing) return;
            // Resolve against the list as last delivered, exactly as the LAN lobby resolves against
            // the live peer table: the row the user tapped was drawn a frame ago.
            row = Array.Find(_open, r => r.Host == peer.Id);
            if (row is null)
            {
                _statusMessage = $"{peer.Label} is no longer open";
                _state = LobbyState.Failed;
                return;
            }
            _state = LobbyState.Inviting;
            _statusMessage = $"Joining {row.PlayerName}…";
        }

        _ = ClaimAsync(row);
    }

    private async Task ClaimAsync(OpenGame row)
    {
        var ourSide = row.PosterSide == Side.White ? Side.Black : Side.White;
        var seat = ourSide == Side.White ? "w" : "b";

        var claimed = await _db.SetAsync($"games/{row.GameId}/{seat}",
            $"{{\"uid\":{CloudJson.Quote(_db.Uid)},\"name\":{CloudJson.Quote(_localName)}}}")
            .ConfigureAwait(false);

        if (!claimed)
        {
            Fail($"{row.PlayerName}'s game was already taken");
            return;
        }

        // A claimed seat cannot be given back — the rules only allow writing a seat that is empty —
        // so from here we are committed, and a Cancel that landed while the write was in flight does
        // not undo it. That is the honest behaviour: somebody now has an opponent.
        Connect(row.GameId, ourSide, row.PlayerName);
    }

    /// <summary>Unreachable: nothing invites us, so there is never an invite to accept.</summary>
    public void Accept() { }

    /// <summary>Unreachable, for the same reason as <see cref="Accept"/>.</summary>
    public void Decline() { }

    public void Cancel()
    {
        lock (_lock)
        {
            if (_state is LobbyState.Connected or LobbyState.Connecting) return;
            _state = LobbyState.Browsing;
            _statusMessage = null;
        }
    }

    // Our own posting: the only thing worth reacting to is the other seat filling.
    private void OnOurGame(string? json)
    {
        using var doc = CloudJson.TryParse(json);
        if (doc is null) return;

        var theirSeat = _preferredColor == Side.Black ? "w" : "b";
        var them = CloudJson.Child(doc.RootElement, theirSeat);
        if (CloudJson.StringOr(them, "uid").Length == 0) return; // still waiting

        var gameId = _gameId;
        if (gameId.Length == 0) return;

        // Stop advertising a game that now has two players. Our own row, so ours to remove; a failure
        // is survivable (the claimer already has the game, and a stale row self-heals).
        _ = _db.RemoveAsync($"open/{_db.Uid}");
        Connect(gameId, _preferredColor, CloudJson.StringOr(them, "name", "Player"),
            known: CloudJson.StringOr(doc.RootElement, "g"));
    }

    private void OnOpenRows(string? json)
    {
        List<OpenGame> rows = [];

        using var doc = CloudJson.TryParse(json);
        if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var member in doc.RootElement.EnumerateObject())
            {
                if (member.Name == _db.Uid) continue; // our own posting
                if (OpenGame.From(member.Name, member.Value) is { } row) rows.Add(row);
            }
        }

        _open = [.. rows.OrderByDescending(r => r.Updated)];
    }

    private void Connect(string gameId, Side ourSide, string peerName, string known = "")
    {
        NetworkSession session;
        lock (_lock)
        {
            if (_state is LobbyState.Connected or LobbyState.Connecting) return;
            _state = LobbyState.Connecting;

            // Our lobby subscription on the game hands over to the connection's own, rather than the
            // two of them delivering every ply twice over.
            _mineWatch?.Dispose();
            _mineWatch = null;

            session = new NetworkSession(new CloudGameConnection(_db, gameId, known), ourSide, peerName);
            _session = session;
            _state = LobbyState.Connected;
        }
    }

    private void Fail(string message)
    {
        lock (_lock)
        {
            if (_state is LobbyState.Connected or LobbyState.Connecting) return;
            _statusMessage = message;
            _state = LobbyState.Failed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        bool connected;
        string gameId;
        lock (_lock)
        {
            connected = _state == LobbyState.Connected;
            gameId = _gameId;
            _openWatch?.Dispose();
            _mineWatch?.Dispose();
            _openWatch = null;
            _mineWatch = null;
        }

        if (_db.Uid.Length == 0 || gameId.Length == 0) return;

        // Withdraw the posting either way; a game nobody claimed goes with it, so leaving the lobby
        // does not leave a row behind on every visit. A game that IS being played stays, obviously —
        // its connection outlives this lobby exactly as a LAN session's socket does.
        await _db.RemoveAsync($"open/{_db.Uid}").ConfigureAwait(false);
        if (!connected)
            await _db.RemoveAsync($"games/{gameId}").ConfigureAwait(false);
    }

    /// <summary>One row of <c>/open</c>: somebody's posted game. <see cref="Host"/> is the poster's
    /// uid, which is also the row's key — so a player holds at most one.</summary>
    private sealed record OpenGame(string Host, string GameId, string Name, string Color, long Updated)
    {
        /// <summary>The colour the POSTER took; whoever joins plays the other one.</summary>
        public Side PosterSide => Color == "b" ? Side.Black : Side.White;

        /// <summary>The poster, for a sentence — "Joining Ana…". Unnamed players are Player, as they
        /// are on the LAN.</summary>
        public string PlayerName => Name.Length == 0 ? "Player" : Name;

        /// <summary>What the lobby screen shows. The colour is part of it because it is the one thing
        /// a joiner cannot choose, and finding out only after joining would be worse.</summary>
        public string Label => $"{PlayerName} — you play {(PosterSide == Side.White ? "Black" : "White")}";

        public bool IsStale(DateTimeOffset now, TimeSpan after) =>
            Updated > 0 && now - DateTimeOffset.FromUnixTimeMilliseconds(Updated) > after;

        public static OpenGame? From(string host, JsonElement row)
        {
            var gameId = CloudJson.StringOr(row, "gameId");
            if (host.Length == 0 || gameId.Length == 0) return null;

            return new OpenGame(host, gameId,
                CloudJson.StringOr(row, "name"),
                CloudJson.StringOr(row, "color"),
                CloudJson.LongOr(row, "updated"));
        }
    }
}
