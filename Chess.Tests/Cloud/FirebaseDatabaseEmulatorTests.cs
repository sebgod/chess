using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.Net;
using Chess.Net.Cloud;
using Shouldly;
using Xunit;

namespace Chess.Tests.Cloud;

/// <summary>
/// The native cloud client against a real database — the only test that exercises the parts a fake
/// cannot stand in for: anonymous sign-in over the Identity Toolkit REST API, the database's own REST
/// verbs, the server-sent event stream and its <c>put</c>/<c>patch</c> framing, and above all the
/// <b>deployed security rules</b>, which are the one piece of this feature's logic that does not live
/// in this repository's code.
///
/// <para><b>Skipped unless the emulator is up</b>, which is why it can sit in the ordinary test
/// project: CI's <c>test</c> job has no emulator and skips these, and the <c>e2e</c> workflow — which
/// already provisions one for the browser suite — runs them. Locally:</para>
/// <code>
///   cd firebase &amp;&amp; npx firebase emulators:start --project demo-chess --only database,auth
/// </code>
/// <para>Nothing here needs a secret, an account or a network: "demo-" is the prefix that makes the
/// emulator run fully offline. It reads the rules straight out of <c>firebase/database.rules.json</c>,
/// so a rule change that would break the native client fails here rather than on a phone.</para>
/// </summary>
public class FirebaseDatabaseEmulatorTests
{
    private const string ConfigJson = """
        {
          "projectId": "demo-chess",
          "emulator": { "auth": "http://127.0.0.1:9099", "host": "127.0.0.1", "port": 9000 }
        }
        """;

    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Probes the PORTS rather than trusting a config, for the reason the browser suite does: a
    /// config pointing at a database nobody is serving fails deep inside a retry loop, and "the cloud
    /// silently did nothing" is the failure shape worth never debugging twice.
    /// </summary>
    private static void RequireEmulator()
    {
        foreach (var (port, what) in new[] { (9000, "database"), (9099, "auth") })
        {
            using var probe = new TcpClient();
            try
            {
                probe.Connect("127.0.0.1", port);
            }
            catch (SocketException)
            {
                Assert.Skip($"The Firebase {what} emulator is not listening on 127.0.0.1:{port} — " +
                            "see this class's summary for the one-line setup.");
            }
        }
    }

    private static async Task<FirebaseDatabase> SignedInAsync()
    {
        var config = FirebaseConfig.TryParse(ConfigJson);
        config.ShouldNotBeNull();

        var db = new FirebaseDatabase(config);
        (await db.SignInAsync()).ShouldBeTrue();
        db.Uid.ShouldNotBeEmpty();
        return db;
    }

    private static async Task WaitForAsync(Func<bool> done, string what)
    {
        var deadline = DateTimeOffset.UtcNow + PushTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (done()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Timed out after {PushTimeout.TotalSeconds:0}s waiting for {what}.");
    }

    [Fact]
    public async Task AnonymousSignIn_GivesEachClientItsOwnIdentity()
    {
        RequireEmulator();

        await using var alice = await SignedInAsync();
        await using var bob = await SignedInAsync();

        // Every rule in the database is written against auth.uid, so two clients sharing one would
        // make the whole suite of seat-ownership tests pass for the wrong reason.
        alice.Uid.ShouldNotBe(bob.Uid);
    }

    [Fact]
    public async Task TwoClients_PlayOneGameThroughTheDatabase()
    {
        RequireEmulator();

        await using var alice = await SignedInAsync();
        await using var bob = await SignedInAsync();

        var gameId = $"t{Guid.NewGuid():N}";
        var path = $"games/{gameId}";

        (await alice.SetAsync(path,
            $$$"""{"g":"","n":0,"w":{"uid":"{{{alice.Uid}}}","name":"Alice"}}""")).ShouldBeTrue();
        (await bob.SetAsync($"{path}/b",
            $$"""{"uid":"{{bob.Uid}}","name":"Bob"}""")).ShouldBeTrue();

        using var whiteConn = new CloudGameConnection(alice, gameId);
        using var blackConn = new CloudGameConnection(bob, gameId);
        using var white = new NetworkSession(whiteConn, Side.White, "Bob");
        using var black = new NetworkSession(blackConn, Side.Black, "Alice");

        white.SendMove("e2e4");
        await WaitForAsync(() => black.HasIncomingMove, "White's move to reach Black");
        black.TryDequeueMove(out var first).ShouldBeTrue();
        first.ShouldBe("e2e4");

        black.SendMove("e7e5");
        await WaitForAsync(() => white.HasIncomingMove, "Black's reply to reach White");
        white.TryDequeueMove(out var reply).ShouldBeTrue();
        reply.ShouldBe("e7e5");

        // The row is a link: `g` is exactly what GameLinkCodec encodes into a #g= fragment.
        var row = await alice.GetAsync(path);
        row.ShouldNotBeNull();
        row.ShouldContain("e2e4.e7e5");
    }

    /// <summary>
    /// The turn gate is the server's, not the client's. This is the assertion that would catch a rules
    /// change silently letting a player move twice — the client cannot notice that on its own, because
    /// its own optimism is exactly what the rules exist to overrule.
    /// </summary>
    [Fact]
    public async Task MovingOutOfTurn_IsRefusedByTheRules()
    {
        RequireEmulator();

        await using var alice = await SignedInAsync();
        var gameId = $"t{Guid.NewGuid():N}";
        var path = $"games/{gameId}";

        (await alice.SetAsync(path,
            $$$"""{"g":"","n":0,"w":{"uid":"{{{alice.Uid}}}","name":"Alice"}}""")).ShouldBeTrue();

        // White's first ply: hers to make.
        (await alice.UpdateAsync(path, """{"g":"e2e4","n":1}""")).ShouldBeTrue();

        // The second is Black's, and she is not Black.
        (await alice.UpdateAsync(path, """{"g":"e2e4.e7e5","n":2}""")).ShouldBeFalse();
    }

    /// <summary>
    /// Append-only, likewise enforced where it cannot be argued with. A client that tried to rewrite
    /// history — a bug, or someone else's client — gets nothing, rather than a shorter game.
    /// </summary>
    [Fact]
    public async Task TruncatingTheMoveLog_IsRefusedByTheRules()
    {
        RequireEmulator();

        await using var alice = await SignedInAsync();
        var gameId = $"t{Guid.NewGuid():N}";
        var path = $"games/{gameId}";

        (await alice.SetAsync(path,
            $$$"""{"g":"e2e4","n":1,"w":{"uid":"{{{alice.Uid}}}","name":"Alice"}}""")).ShouldBeTrue();

        (await alice.UpdateAsync(path, """{"g":"d2d4","n":1}""")).ShouldBeFalse();
        (await alice.GetAsync($"{path}/g")).ShouldBe("\"e2e4\"");
    }

    /// <summary>
    /// A subscription's first callback is the current value, which is what makes resuming a game two
    /// days later the same code path as joining a fresh one — and it is delivered off the event
    /// stream, so this also proves the stream's opening <c>put</c> is parsed.
    /// </summary>
    [Fact]
    public async Task Subscribing_DeliversTheCurrentValueFirst()
    {
        RequireEmulator();

        await using var alice = await SignedInAsync();
        var gameId = $"t{Guid.NewGuid():N}";
        var path = $"games/{gameId}";

        (await alice.SetAsync(path,
            $$$"""{"g":"e2e4.e7e5","n":2,"w":{"uid":"{{{alice.Uid}}}","name":"Alice"}}""")).ShouldBeTrue();

        List<string> heard = [];
        using var conn = new CloudGameConnection(alice, gameId);
        conn.LineReceived += line => heard.Add(SessionProtocol.Parse(line).Move);
        conn.StartReceiving();

        await WaitForAsync(() => heard.Count >= 2, "the stored game to replay");
        heard.ShouldBe(["e2e4", "e7e5"]);
    }
}
