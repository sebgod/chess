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
