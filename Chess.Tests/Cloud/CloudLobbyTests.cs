using System;
using System.Linq;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.Net;
using Chess.Net.Cloud;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Chess.Tests.Cloud;

/// <summary>
/// The cloud lobby driven exactly as a front-end drives it — <c>Start()</c>, read <c>Peers</c>,
/// <c>Invite</c>, take <c>Session</c> — over an in-memory database (see <see cref="FakeCloudStore"/>).
/// The point of these is that they are the same six calls <c>LanLobbyTests</c> makes, which is what
/// <see cref="ILobby"/> was extracted for.
/// </summary>
public class CloudLobbyTests
{
    private static CloudLobby Player(FakeCloudStore store, string uid, string name, Side preferred, TimeProvider time)
        => new(new FakeCloudDatabase(store, uid), name, preferred, time);

    private static (FakeCloudStore Store, CloudLobby Alice, CloudLobby Bob) TwoPlayers(
        Side aliceColor = Side.White, FakeTimeProvider? time = null)
    {
        var clock = time ?? new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var store = new FakeCloudStore { Time = clock };
        var alice = Player(store, "alice", "Alice", aliceColor, clock);
        var bob = Player(store, "bob", "Bob", Side.White, clock);
        alice.Start(); // each Start() posts an open game the other's lobby then lists
        bob.Start();
        return (store, alice, bob);
    }

    /// <summary>
    /// Being visible is what Start() does on the LAN — it announces you, and anyone may invite you.
    /// <c>/open/{uid}</c> is that announcement persisted, so the cloud's Start() creates the game and
    /// advertises it. Nothing else in the interface has to grow a "post" verb for that.
    /// </summary>
    [Fact]
    public async Task Start_AdvertisesAGameWithOurSeatTaken()
    {
        var (store, alice, bob) = TwoPlayers();
        await using var _a = alice; await using var _b = bob;

        var row = store.Read("open/alice");
        row.ShouldNotBeNull();
        row.ShouldContain("\"name\":\"Alice\"");
        row.ShouldContain("\"color\":\"w\"");

        var gameId = System.Text.Json.JsonDocument.Parse(row).RootElement.GetProperty("gameId").GetString();
        var game = store.Read($"games/{gameId}");
        game.ShouldNotBeNull();
        game.ShouldContain("\"uid\":\"alice\"");
        game.ShouldNotContain("\"uid\":\"bob\"");   // the other seat is the invitation
    }

    [Fact]
    public async Task Peers_ListOtherPlayers_ButNotOurself()
    {
        var (_, alice, bob) = TwoPlayers();
        await using var _a = alice; await using var _b = bob;

        // You cannot be your own opponent, and the rules would happily let you take both seats.
        alice.Peers.Select(p => p.Id).ShouldBe(["bob"]);
        bob.Peers.Select(p => p.Id).ShouldBe(["alice"]);
    }

    /// <summary>The colour is in the label because it is the one thing a joiner cannot choose, and
    /// finding out after joining would be worse.</summary>
    [Fact]
    public async Task PeerLabel_SaysWhichColourTheJoinerGets()
    {
        var (_, alice, bob) = TwoPlayers(aliceColor: Side.Black);
        await using var _a = alice; await using var _b = bob;

        bob.Peers.Single().Label.ShouldBe("Alice — you play White");
    }

    [Fact]
    public async Task Claim_ConnectsBothSides_WithOppositeColours()
    {
        var (_, alice, bob) = TwoPlayers(aliceColor: Side.White);
        await using var _a = alice; await using var _b = bob;

        bob.Invite(bob.Peers.Single(p => p.Id == "alice"));

        alice.State.ShouldBe(LobbyState.Connected);
        bob.State.ShouldBe(LobbyState.Connected);
        alice.Session!.LocalSide.ShouldBe(Side.White);
        bob.Session!.LocalSide.ShouldBe(Side.Black);
        alice.Session!.PeerName.ShouldBe("Bob");
        bob.Session!.PeerName.ShouldBe("Alice");
    }

    [Fact]
    public async Task ConnectedSessions_ExchangeMovesBothWays()
    {
        var (_, alice, bob) = TwoPlayers();
        await using var _a = alice; await using var _b = bob;

        bob.Invite(bob.Peers.Single(p => p.Id == "alice"));

        alice.Session!.SendMove("e2e4");
        bob.Session!.TryDequeueMove(out var first).ShouldBeTrue();
        first.ShouldBe("e2e4");

        bob.Session!.SendMove("e7e5");
        alice.Session!.TryDequeueMove(out var reply).ShouldBeTrue();
        reply.ShouldBe("e7e5");
    }

    /// <summary>
    /// Only the poster may write their own <c>/open</c> row, so withdrawing a filled posting is the
    /// poster's job — done the moment they see their seat taken. Without it the lobby would advertise
    /// a game that cannot be joined.
    /// </summary>
    [Fact]
    public async Task FilledPosting_IsWithdrawnByThePoster()
    {
        var (store, alice, bob) = TwoPlayers();
        await using var _a = alice; await using var _b = bob;

        bob.Invite(bob.Peers.Single(p => p.Id == "alice"));

        store.Read("open/alice").ShouldBeNull();
    }

    /// <summary>
    /// Two players racing for one seat is resolved by the rules, server-side, and the loser has to be
    /// told rather than left staring at a lobby. (The fake stands in for the rule here; that the real
    /// one behaves this way is asserted against the emulator.)
    /// </summary>
    [Fact]
    public async Task Claim_RefusedBecauseTheSeatWentFirst_ReportsFailure()
    {
        var (store, alice, bob) = TwoPlayers();
        await using var _a = alice; await using var _b = bob;

        var peer = bob.Peers.Single(p => p.Id == "alice");
        store.Refuse = (path, _) => path.EndsWith("/b", StringComparison.Ordinal);
        bob.Invite(peer);

        bob.State.ShouldBe(LobbyState.Failed);
        bob.StatusMessage.ShouldBe("Alice's game was already taken");
        bob.Session.ShouldBeNull();
        alice.State.ShouldBe(LobbyState.Browsing); // nobody joined her
    }

    /// <summary>
    /// Postings are durable on purpose — a posted correspondence game has to outlive the app that
    /// posted it — so nothing on the server removes an abandoned one and the client hides it. An
    /// unfiltered lobby would silently fill up with invitations nobody is waiting behind.
    /// </summary>
    [Fact]
    public async Task Peers_HidePostingsNobodyHasTouchedForAWeek()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var (_, alice, bob) = TwoPlayers(time: clock);
        await using var _a = alice; await using var _b = bob;

        bob.Peers.ShouldHaveSingleItem();

        clock.Advance(TimeSpan.FromDays(8));

        // No new delivery: the row ages where it sits, and the lobby it is listed in is looking at
        // it every frame. Judging staleness on arrival would have kept it on screen until somebody
        // ELSE posted.
        bob.Peers.ShouldBeEmpty();
    }

    /// <summary>
    /// Leaving the lobby without a game takes the posting AND the game row with it. Otherwise every
    /// visit would leave a one-seat game behind for ever, on a free tier whose whole appeal is that
    /// it caps rather than bills.
    /// </summary>
    [Fact]
    public async Task LeavingUnclaimed_TakesBothRowsWithIt()
    {
        var (store, alice, bob) = TwoPlayers();
        await using var _b = bob;

        var gameId = System.Text.Json.JsonDocument
            .Parse(store.Read("open/alice")!).RootElement.GetProperty("gameId").GetString();

        await alice.DisposeAsync();

        store.Read("open/alice").ShouldBeNull();
        store.Read($"games/{gameId}").ShouldBeNull();
    }

    /// <summary>…but a game being played survives the lobby that produced it, exactly as a LAN
    /// session's socket does.</summary>
    [Fact]
    public async Task LeavingAfterConnecting_KeepsTheGame()
    {
        var (store, alice, bob) = TwoPlayers();
        await using var _b = bob;

        var gameId = System.Text.Json.JsonDocument
            .Parse(store.Read("open/alice")!).RootElement.GetProperty("gameId").GetString();
        bob.Invite(bob.Peers.Single(p => p.Id == "alice"));

        await alice.DisposeAsync();

        store.Read($"games/{gameId}").ShouldNotBeNull();
    }
}
