using System.Net.Sockets;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// Browser E2E for cloud correspondence play: two players, two browser contexts, one game carried
/// by the database instead of by a link.
///
/// <para><b>Two contexts, not two tabs.</b> Anonymous sign-in is persisted per origin, so a second
/// tab in the same profile signs in as the same uid and simply takes both seats. That would still
/// show moves appearing and would prove nothing about rules that are entirely about who you are —
/// <c>NewPageAsync</c> gives each player its own storage, hence its own identity.</para>
///
/// <para><b>Skipped unless a local backend is up</b>, because unlike the rest of this suite these
/// need one. Two things, both local and free:</para>
/// <code>
///   cd firebase &amp;&amp; PATH="&lt;a JDK 21+&gt;/bin:$PATH" npx firebase emulators:start \
///       --project demo-chess --only database,auth
///
///   cat &gt; Chess.Web/wwwroot/firebase-config.json &lt;&lt;'EOF'
///   { "projectId": "demo-chess",
///     "emulator": { "auth": "http://127.0.0.1:9099", "host": "127.0.0.1", "port": 9000 } }
///   EOF
/// </code>
/// <para>That file is gitignored and points at nothing real, so it costs nothing to leave in place.
/// Against the live project the same tests pass with the live config instead — but they write to a
/// database other people can be playing on, so the emulator is the default.</para>
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class CloudPlayTests(ChessWebFixture fixture)
{
    private const float BootTimeout = 60_000;   // WASM cold boot dwarfs any DOM settle time
    private const float PushTimeout = 20_000;   // a push is a round trip through the database

    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "Chess.Web", "wwwroot", "firebase-config.json");

    /// <summary>
    /// Skips the test unless both halves of a local backend are present. Deliberately checks the
    /// emulator PORT rather than trusting the config file: a config pointing at a database nobody
    /// is serving fails deep inside the SDK, and "cloud silently did nothing" is the one failure
    /// shape worth never debugging twice.
    /// </summary>
    private static void RequireLocalBackend()
    {
        Assert.SkipUnless(File.Exists(ConfigPath),
            $"No {Path.GetFullPath(ConfigPath)} — see this class's summary for the two-line setup.");

        using var probe = new TcpClient();
        try
        {
            probe.Connect("127.0.0.1", 9000);
        }
        catch (SocketException)
        {
            Assert.Skip("The Firebase database emulator is not listening on 127.0.0.1:9000.");
        }
    }

    private ILocator Status(IPage page) => page.Locator(".status");

    private async Task<IPage> JoinAsync(string gameId)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}#c={gameId}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });
        return page;
    }

    private static async Task PlayMoveAsync(IPage page, string uci)
    {
        await page.Locator("#board").FocusAsync();
        foreach (var ch in uci)
        {
            await page.Keyboard.PressAsync(ch.ToString());
            await page.WaitForTimeoutAsync(60); // let Blazor's async keydown handler settle
        }
    }

    // A fresh id per run: these tests write real rows, and reusing one would make a rerun join a
    // finished game instead of starting a new one.
    private static string NewGameId() => "e2e" + DateTime.UtcNow.Ticks.ToString("x");

    [Fact]
    public async Task TwoPlayersJoinByLinkAndMovesTravelBothWays()
    {
        RequireLocalBackend();
        var gameId = NewGameId();

        // First in creates the game and takes White; until someone else arrives there is nobody to
        // play, and the status says so rather than pretending the game has started.
        var white = await JoinAsync(gameId);
        await Expect(Status(white)).ToContainTextAsync("Waiting for an opponent",
            new() { Timeout = BootTimeout });

        // Second in claims the seat left empty. Nothing was coordinated between them but the link.
        var black = await JoinAsync(gameId);
        await Expect(Status(black)).ToContainTextAsync("Player", new() { Timeout = BootTimeout });

        // The seat filling is a push, not a reload: White's page learns it has an opponent.
        await Expect(Status(white)).ToContainTextAsync("Your move (White)",
            new() { Timeout = PushTimeout });

        await PlayMoveAsync(white, "e2e4");
        await Expect(Status(black)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = PushTimeout });

        await PlayMoveAsync(black, "e7e5");
        await Expect(Status(white)).ToContainTextAsync("Your move (White)",
            new() { Timeout = PushTimeout });
    }

    [Fact]
    public async Task TheAddressBarNamesTheGameRatherThanCarryingIt()
    {
        RequireLocalBackend();
        var gameId = NewGameId();

        var white = await JoinAsync(gameId);
        await Expect(Status(white)).ToContainTextAsync("Waiting for an opponent",
            new() { Timeout = BootTimeout });

        var black = await JoinAsync(gameId);
        await Expect(Status(white)).ToContainTextAsync("Your move (White)",
            new() { Timeout = BootTimeout });

        await PlayMoveAsync(white, "e2e4");
        await Expect(Status(black)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = PushTimeout });

        // The whole difference between the couriers, in one assertion: a link game rewrites the
        // fragment on every ply because the fragment IS the game, and a cloud game never does
        // because the fragment only names it. So this link stays valid and stays short forever.
        Assert.EndsWith($"#c={gameId}", white.Url, StringComparison.Ordinal);
        Assert.EndsWith($"#c={gameId}", black.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThirdPlayerIsTurnedAwayFromAFullGame()
    {
        RequireLocalBackend();
        var gameId = NewGameId();

        var white = await JoinAsync(gameId);
        await Expect(Status(white)).ToContainTextAsync("Waiting for an opponent",
            new() { Timeout = BootTimeout });

        var black = await JoinAsync(gameId);
        await Expect(Status(white)).ToContainTextAsync("Your move (White)",
            new() { Timeout = BootTimeout });

        // Both seats are taken, and the rules will not let a third identity read the game either —
        // so the app cannot show them the board even as a spectator, and says so instead of
        // hanging on "Connecting…".
        var gatecrasher = await JoinAsync(gameId);
        await Expect(Status(gatecrasher)).ToContainTextAsync("already has two players",
            new() { Timeout = BootTimeout });
    }
}
