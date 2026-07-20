using System.Linq;
using Chess.Lib;
using Chess.Net;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanLobbyTests
{
    private static LanLobby MakePeer(
        FakeLanBus bus, FakeTimeProvider time, string address, int port, string peerId, string name, Side preferred)
    {
        var transport = bus.CreateNode(address, port);
        var identity = new LanIdentity(name, peerId);
        var discovery = new LanDiscovery(transport, time, peerId, () => identity.Name);
        return new LanLobby(transport, discovery, identity, preferred);
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
    public void BothStart_SeeEachOther()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider());
        using var _a = alice; using var _b = bob;

        alice.Peers.Select(p => p.PeerId).ShouldContain("bob");
        bob.Peers.Select(p => p.PeerId).ShouldContain("alice");
    }

    [Fact]
    public void Invite_Accept_BothConnected_WithOppositeColors()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider(), aliceColor: Side.White);
        using var _a = alice; using var _b = bob;

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
    public void InviterChoosingBlack_MakesInviteeWhite()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider(), aliceColor: Side.Black);
        using var _a = alice; using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));
        bob.Incoming!.YourSide.ShouldBe(Side.White);
        bob.Accept();

        alice.Session!.LocalSide.ShouldBe(Side.Black);
        bob.Session!.LocalSide.ShouldBe(Side.White);
    }

    [Fact]
    public void ConnectedSessions_ExchangeMovesBothWays()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider());
        using var _a = alice; using var _b = bob;

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
    public void Invite_Decline_InviterDeclined_InviteeBrowsing()
    {
        var bus = new FakeLanBus();
        var (alice, bob) = TwoVisiblePeers(bus, new FakeTimeProvider());
        using var _a = alice; using var _b = bob;

        alice.Invite(alice.Peers.Single(p => p.PeerId == "bob"));
        bob.State.ShouldBe(LobbyState.IncomingInvite);

        bob.Decline();

        alice.State.ShouldBe(LobbyState.Declined);
        bob.State.ShouldBe(LobbyState.Browsing);
        alice.Session.ShouldBeNull();
    }
}
