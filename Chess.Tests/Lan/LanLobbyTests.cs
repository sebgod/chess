using System.Linq;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.Net;
using LAN.Lib;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanLobbyTests
{
    // The same two-layer wiring LanPlayStack does for real (LAN.Lib discovery + chess's session
    // transport), but over the in-memory bus and with an explicit peer id so the assertions can name
    // the peers. Announces chess's service, which is also what the lobby filters its peer list on.
    private static LanLobby MakePeer(
        FakeLanBus bus, FakeTimeProvider time, string address, int port, string peerId, string name, Side preferred)
    {
        var node = bus.CreateNode(address, port);
        var identity = new LanIdentity(peerId, NodeId: "");
        var options = new LanDiscoveryOptions
        {
            ServiceName = SessionProtocol.ServiceName,
            ServicePort = port,
            NodeName = name,
        };
        var discovery = new LanDiscovery(node.Discovery, time, options, identity);
        return new LanLobby(node.Session, discovery, identity, name, preferred);
    }

    private static (LanLobby Alice, LanLobby Bob) TwoVisiblePeers(
        FakeLanBus bus, FakeTimeProvider time, Side aliceColor = Side.White)
    {
        var alice = MakePeer(bus, time, "192.168.1.10", 40001, "alice", "Alice", aliceColor);
        var bob = MakePeer(bus, time, "192.168.1.20", 40002, "bob", "Bob", Side.White);
        alice.Start(); // each Start() broadcasts an announce the other's discovery records
        bob.Start();
        return (alice, bob);
    }

    [Fact]
    public async Task BothStart_SeeEachOther()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider());
        await using var _a = alice; await using var _b = bob;

        alice.Peers.Select(p => p.PeerId).ShouldContain("bob");
        bob.Peers.Select(p => p.PeerId).ShouldContain("alice");
    }

    [Fact]
    public async Task Invite_Accept_BothConnected_WithOppositeColors()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider(), aliceColor: Side.White);
        await using var _a = alice; await using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));

        // Alice (inviter) chose White, so Bob is offered Black.
        bob.State.ShouldBe(LobbyState.IncomingInvite);
        bob.Incoming.ShouldNotBeNull();
        bob.Incoming!.PeerName.ShouldBe("Alice");
        bob.Incoming!.YourSide.ShouldBe(Side.Black);

        bob.Accept();

        alice.State.ShouldBe(LobbyState.Connected);
        bob.State.ShouldBe(LobbyState.Connected);
        alice.Session.ShouldNotBeNull();
        bob.Session.ShouldNotBeNull();
        alice.Session!.LocalSide.ShouldBe(Side.White);
        bob.Session!.LocalSide.ShouldBe(Side.Black);
        alice.Session!.PeerName.ShouldBe("Bob");
        bob.Session!.PeerName.ShouldBe("Alice");
    }

    [Fact]
    public async Task InviterChoosingBlack_MakesInviteeWhite()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider(), aliceColor: Side.Black);
        await using var _a = alice; await using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));
        bob.Incoming!.YourSide.ShouldBe(Side.White);
        bob.Accept();

        alice.Session!.LocalSide.ShouldBe(Side.Black);
        bob.Session!.LocalSide.ShouldBe(Side.White);
    }

    [Fact]
    public async Task ConnectedSessions_ExchangeMovesBothWays()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider());
        await using var _a = alice; await using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));
        bob.Accept();

        alice.Session!.SendMove("e2e4");
        bob.Session!.TryDequeueMove(out var m1).ShouldBeTrue();
        m1.ShouldBe("e2e4");

        bob.Session!.SendMove("e7e5");
        alice.Session!.TryDequeueMove(out var m2).ShouldBeTrue();
        m2.ShouldBe("e7e5");
    }

    /// <summary>
    /// The host starts the game — and may immediately send a move — the moment the lobby says
    /// <c>Connected</c>. So ACCEPT has to be on the wire BEFORE that, or the invitee's first move can
    /// overtake it and be dropped by an inviter still waiting in <c>Inviting</c>. Alice invites as
    /// Black here, which is the case that bites: it makes Bob (the accepter) White, so his host is the
    /// one with a move to make straight away.
    /// </summary>
    [Fact]
    public async Task Accept_SendsAcceptBeforePublishingConnected()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider(), aliceColor: Side.Black);
        await using var _a = alice; await using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));
        bob.Incoming!.YourSide.ShouldBe(Side.White);

        LobbyState? stateAtAccept = null;
        var sessionVisibleAtAccept = true;
        bus.SessionSending = line =>
        {
            if (SessionProtocol.Parse(line).Kind != SessionMessageKind.Accept) return;
            stateAtAccept = bob.State;
            sessionVisibleAtAccept = bob.Session is not null;
        };

        bob.Accept();

        // What a host polling Bob's lobby could have seen at the instant ACCEPT went out: not yet
        // Connected, and no Session to take — so there was no way to race a move ahead of it.
        stateAtAccept.ShouldBe(LobbyState.Connecting);
        sessionVisibleAtAccept.ShouldBeFalse();

        // …and once the send returns, the handshake completes as before.
        bob.State.ShouldBe(LobbyState.Connected);
        bob.Session.ShouldNotBeNull();
        alice.State.ShouldBe(LobbyState.Connected);

        // Bob is White: his first move must reach Alice, which is the whole point of the ordering.
        bob.Session!.SendMove("e2e4");
        alice.Session!.TryDequeueMove(out var first).ShouldBeTrue();
        first.ShouldBe("e2e4");
    }

    [Fact]
    public async Task Invite_Decline_InviterDeclined_InviteeBrowsing()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider());
        await using var _a = alice; await using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));
        bob.State.ShouldBe(LobbyState.IncomingInvite);

        bob.Decline();

        alice.State.ShouldBe(LobbyState.Declined);
        bob.State.ShouldBe(LobbyState.Browsing);
        alice.Session.ShouldBeNull();
    }
}
