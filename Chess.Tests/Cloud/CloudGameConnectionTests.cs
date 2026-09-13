using System.Collections.Generic;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.Net;
using Chess.Net.Cloud;
using Shouldly;
using Xunit;

namespace Chess.Tests.Cloud;

/// <summary>
/// The cloud courier's channel, tested with no cloud — the store is in memory and delivers
/// synchronously, so an exchange between two clients is deterministic (see
/// <see cref="FakeCloudStore"/>).
///
/// <para>What is under test is the one thing this class exists for: a database row holding a shared,
/// append-only move log, presented to <see cref="NetworkSession"/> as the stream of moves every
/// caller in <c>Chess.Net</c> already expects.</para>
/// </summary>
public class CloudGameConnectionTests
{
    private const string GameId = "g1";
    private const string Path = $"games/{GameId}";

    private static (FakeCloudStore Store, CloudGameConnection White, CloudGameConnection Black) TwoSeats()
    {
        var store = new FakeCloudStore();
        store.Set(Path, """{"g":"","n":0,"w":{"uid":"alice","name":"Alice"},"b":{"uid":"bob","name":"Bob"}}""");
        return (store,
            new CloudGameConnection(new FakeCloudDatabase(store, "alice"), GameId),
            new CloudGameConnection(new FakeCloudDatabase(store, "bob"), GameId));
    }

    private static List<string> Collect(CloudGameConnection conn)
    {
        List<string> lines = [];
        conn.LineReceived += lines.Add;
        conn.StartReceiving();
        return lines;
    }

    [Fact]
    public void Move_ReachesTheOtherSeat_AsAProtocolLine()
    {
        var (_, white, black) = TwoSeats();
        var heard = Collect(black);
        Collect(white);

        white.Send(SessionProtocol.EncodeMove("e2e4"));

        heard.ShouldHaveSingleItem();
        SessionProtocol.Parse(heard[0]).Kind.ShouldBe(SessionMessageKind.Move);
        SessionProtocol.Parse(heard[0]).Move.ShouldBe("e2e4");
    }

    /// <summary>
    /// The trap this class is built around. A subscription echoes our OWN append back, and a
    /// connection that surfaced it would hand the host its own move as the opponent's — which the
    /// rules engine would then reject, or worse, accept. Nothing arrives at the sender.
    /// </summary>
    [Fact]
    public void OwnMove_IsNotSurfacedBackToTheSender()
    {
        var (_, white, black) = TwoSeats();
        var mine = Collect(white);
        Collect(black);

        white.Send(SessionProtocol.EncodeMove("e2e4"));

        mine.ShouldBeEmpty();
    }

    [Fact]
    public void Moves_AlternateWithoutEitherSeatHearingItself()
    {
        var (store, white, black) = TwoSeats();
        var atWhite = Collect(white);
        var atBlack = Collect(black);

        white.Send(SessionProtocol.EncodeMove("e2e4"));
        black.Send(SessionProtocol.EncodeMove("e7e5"));
        white.Send(SessionProtocol.EncodeMove("g1f3"));

        atWhite.Count.ShouldBe(1);
        SessionProtocol.Parse(atWhite[0]).Move.ShouldBe("e7e5");
        atBlack.Count.ShouldBe(2);
        SessionProtocol.Parse(atBlack[0]).Move.ShouldBe("e2e4");
        SessionProtocol.Parse(atBlack[1]).Move.ShouldBe("g1f3");

        // The log is one string that only ever grows, which is exactly what the append-only rule
        // polices server-side — and what makes the row a link, decodable by GameLinkCodec.
        store.Read($"{Path}/g").ShouldBe("\"e2e4.e7e5.g1f3\"");
        store.Read($"{Path}/n").ShouldBe("3");
    }

    /// <summary>
    /// A row carries the whole game rather than the plies since you last looked, so opening a game in
    /// progress replays it. That is what makes correspondence play work at all: the app was closed for
    /// two days and nothing was buffered anywhere on its behalf.
    /// </summary>
    [Fact]
    public void OpeningAGameInProgress_ReplaysEveryPly()
    {
        var store = new FakeCloudStore();
        store.Set(Path, """{"g":"e2e4.e7e5.g1f3","n":3}""");

        var conn = new CloudGameConnection(new FakeCloudDatabase(store, "alice"), GameId);
        var heard = Collect(conn);

        heard.Count.ShouldBe(3);
        SessionProtocol.Parse(heard[0]).Move.ShouldBe("e2e4");
        SessionProtocol.Parse(heard[2]).Move.ShouldBe("g1f3");
    }

    /// <summary>…and a caller that already holds part of the game says so, and hears only the rest.</summary>
    [Fact]
    public void OpeningWithMovesAlreadyKnown_ReplaysOnlyTheRemainder()
    {
        var store = new FakeCloudStore();
        store.Set(Path, """{"g":"e2e4.e7e5.g1f3","n":3}""");

        var conn = new CloudGameConnection(new FakeCloudDatabase(store, "alice"), GameId, known: "e2e4.e7e5");
        var heard = Collect(conn);

        heard.ShouldHaveSingleItem();
        SessionProtocol.Parse(heard[0]).Move.ShouldBe("g1f3");
    }

    /// <summary>
    /// A refused append is a legitimate outcome — the opponent's ply landed first, or it was never our
    /// turn — and the server is the arbiter. What must not happen is the client going on believing the
    /// ply was taken: the next one would then extend a log the server never held, and the rules would
    /// refuse that too, silently and forever.
    /// </summary>
    [Fact]
    public async Task RefusedAppend_IsGivenBack_SoTheNextOneStillFits()
    {
        var store = new FakeCloudStore();
        store.Set(Path, """{"g":"","n":0}""");
        var db = new FakeCloudDatabase(store, "alice");
        var conn = new CloudGameConnection(db, GameId);
        Collect(conn);

        store.Refuse = (_, _) => true;
        conn.Send(SessionProtocol.EncodeMove("e2e4"));
        await Task.Yield();

        store.Refuse = null;
        conn.Send(SessionProtocol.EncodeMove("d2d4"));
        await Task.Yield();

        // Not "e2e4.d2d4": the refused ply was never ours to build on.
        store.Read($"{Path}/g").ShouldBe("\"d2d4\"");
        store.Read($"{Path}/n").ShouldBe("1");
    }

    /// <summary>
    /// Append-only is a server rule, and a client that finds the stored history is not an extension of
    /// its own is looking at a game it can no longer reason about. Closing unwinds the host to the
    /// menu, which is the honest outcome; playing on would mean moving on a board that no longer
    /// matches the one of record.
    /// </summary>
    [Fact]
    public void HistoryThatIsNotAnExtensionOfOurs_ClosesTheConnection()
    {
        var store = new FakeCloudStore();
        store.Set(Path, """{"g":"e2e4","n":1}""");
        var conn = new CloudGameConnection(new FakeCloudDatabase(store, "alice"), GameId);
        Collect(conn);

        var closed = false;
        conn.Closed += () => closed = true;

        store.Set(Path, """{"g":"d2d4","n":1}""");

        closed.ShouldBeTrue();
        conn.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public void GameThatVanishes_ClosesTheConnection()
    {
        var store = new FakeCloudStore();
        store.Set(Path, """{"g":"","n":0}""");
        var conn = new CloudGameConnection(new FakeCloudDatabase(store, "alice"), GameId);
        Collect(conn);

        var closed = false;
        conn.Closed += () => closed = true;

        store.Remove(Path);

        closed.ShouldBeTrue();
    }

    /// <summary>
    /// The point of the whole adapter: <see cref="NetworkSession"/> — and so <c>NetworkPlayer</c>, and
    /// so every host's drain loop — works over a database row without knowing there is one.
    /// </summary>
    [Fact]
    public void NetworkSession_RunsOverACloudRowUnchanged()
    {
        var (_, whiteConn, blackConn) = TwoSeats();
        using var white = new NetworkSession(whiteConn, Side.White, "Bob");
        using var black = new NetworkSession(blackConn, Side.Black, "Alice");

        white.SendMove("e2e4");
        black.TryDequeueMove(out var got).ShouldBeTrue();
        got.ShouldBe("e2e4");
        white.HasIncomingMove.ShouldBeFalse(); // its own move did not come back

        black.SendMove("e7e5");
        white.TryDequeueMove(out var reply).ShouldBeTrue();
        reply.ShouldBe("e7e5");
    }
}
