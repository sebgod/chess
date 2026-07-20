using System;
using System.Net;
using Chess.Net;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanDiscoveryTests
{
    private const string LocalId = "local-peer";

    private static LanDiscovery NewDiscovery(FakeLanTransport transport, FakeTimeProvider time, string name = "Me")
        => new(transport, time, LocalId, () => name);

    [Fact]
    public void RemoteAnnounce_Appears_WithSenderAddressAndAnnouncedPort()
    {
        var bus = new FakeLanBus();
        var time = new FakeTimeProvider();
        var local = bus.CreateNode("192.168.1.10", 40000);
        using var discovery = NewDiscovery(local, time);
        discovery.Start();

        local.DeliverDatagram(new DiscoveryDatagram(
            LanProtocol.EncodeAnnounce("remote-1", 55555, "Alice"),
            IPAddress.Parse("192.168.1.20")));

        var peers = discovery.Peers;
        peers.Count.ShouldBe(1);
        peers[0].PeerId.ShouldBe("remote-1");
        peers[0].Name.ShouldBe("Alice");
        // Host comes from the datagram's sender; port comes from the announce payload.
        peers[0].EndPoint.ShouldBe(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 55555));
    }

    [Fact]
    public void OwnAnnounce_IsIgnored()
    {
        var bus = new FakeLanBus();
        var time = new FakeTimeProvider();
        var local = bus.CreateNode("192.168.1.10", 40000);
        using var discovery = NewDiscovery(local, time);

        // Start() broadcasts our own announce, which the bus echoes back to us like real UDP does.
        discovery.Start();

        discovery.Peers.ShouldBeEmpty();
    }

    [Fact]
    public void Bye_RemovesPeer()
    {
        var bus = new FakeLanBus();
        var time = new FakeTimeProvider();
        var local = bus.CreateNode("192.168.1.10", 40000);
        using var discovery = NewDiscovery(local, time);
        discovery.Start();

        local.DeliverDatagram(new DiscoveryDatagram(
            LanProtocol.EncodeAnnounce("remote-1", 1, "Alice"), IPAddress.Parse("192.168.1.20")));
        discovery.Peers.Count.ShouldBe(1);

        local.DeliverDatagram(new DiscoveryDatagram(
            LanProtocol.EncodeBye("remote-1"), IPAddress.Parse("192.168.1.20")));
        discovery.Peers.ShouldBeEmpty();
    }

    [Fact]
    public void StalePeer_ExpiresAfterTimeout()
    {
        var bus = new FakeLanBus();
        var time = new FakeTimeProvider();
        var local = bus.CreateNode("192.168.1.10", 40000);
        using var discovery = NewDiscovery(local, time);
        discovery.Start();

        local.DeliverDatagram(new DiscoveryDatagram(
            LanProtocol.EncodeAnnounce("remote-1", 1, "Alice"), IPAddress.Parse("192.168.1.20")));
        discovery.Peers.Count.ShouldBe(1);

        // Advance past the timeout; the beacon timer fires prune along the way.
        time.Advance(LanDiscovery.PeerTimeout + LanDiscovery.BeaconInterval);

        discovery.Peers.ShouldBeEmpty();
    }

    [Fact]
    public void ReAnnounce_KeepsPeerAlive()
    {
        var bus = new FakeLanBus();
        var time = new FakeTimeProvider();
        var local = bus.CreateNode("192.168.1.10", 40000);
        using var discovery = NewDiscovery(local, time);
        discovery.Start();

        // Refresh every 2s (< the 5s timeout) — the peer must never expire.
        for (var i = 0; i < 10; i++)
        {
            local.DeliverDatagram(new DiscoveryDatagram(
                LanProtocol.EncodeAnnounce("remote-1", 1, "Alice"), IPAddress.Parse("192.168.1.20")));
            time.Advance(TimeSpan.FromSeconds(2));
        }

        discovery.Peers.Count.ShouldBe(1);
    }
}
