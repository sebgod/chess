using System;
using System.IO;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanIdentityTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "chess-lan-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsNameAndPeerId()
    {
        var dir = TempDir();
        try
        {
            new LanIdentity("Alice", "peer-123").Save(dir);

            var loaded = LanIdentity.Load(dir);
            loaded.Name.ShouldBe("Alice");
            loaded.PeerId.ShouldBe("peer-123");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_Missing_MintsFreshIdWithEmptyName()
    {
        var id = LanIdentity.Load(TempDir()); // directory doesn't exist

        id.Name.ShouldBe("");
        id.PeerId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Load_TwiceWithoutSave_MintsDistinctIds()
    {
        LanIdentity.Load(TempDir()).PeerId.ShouldNotBe(LanIdentity.Load(TempDir()).PeerId);
    }
}
