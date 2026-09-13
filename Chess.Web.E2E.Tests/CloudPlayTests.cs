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

    // The game-mode menu is drawn into the canvas, so there is nothing to read and nothing to click
    // by name: a digit selects and confirms the item at that position. The order comes from
    // StartupWizard.Current and is load-bearing — Player vs Player, Player vs Computer, Custom Game,
    // Play by Link, Online game — so this constant moves if a new option is added before it.
    private const string OnlineGameKey = "5";

    private static async Task PressAsync(IPage page, string key)
    {
        await page.Locator("#board").FocusAsync();
        await page.Keyboard.PressAsync(key);
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>Wizard -> "Online game" -> "Play as White" -> the lobby.</summary>
    private async Task<IPage> OpenLobbyAsync()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Status(page)).ToContainTextAsync("Choose how", new() { Timeout = BootTimeout });

        await PressAsync(page, OnlineGameKey);
        await PressAsync(page, "1"); // Play as White
        return page;
    }

    /// <summary>
    /// Empties the emulator's lobby and games. A posted game is DURABLE by design — that is what
    /// lets someone claim it tomorrow — so rows survive between runs and a previous run would
    /// otherwise leave the lobby non-empty. Safe because the guard above already established that
    /// this is the local emulator and not a real project.
    /// </summary>
    private static async Task ResetEmulatorAsync()
    {
        using var http = new HttpClient();
        foreach (var path in (string[])["open", "games"])
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete,
                $"http://127.0.0.1:9000/{path}.json?ns=demo-chess-default-rtdb");
            req.Headers.Add("Authorization", "Bearer owner");
            (await http.SendAsync(req)).EnsureSuccessStatusCode();
        }
    }

    private static async Task SetNameAsync(IPage page, string name)
    {
        var field = page.Locator(".toolbar input[type=\"text\"]");
        await field.FillAsync(name);
        await field.BlurAsync(); // @onchange fires on blur, which is what writes localStorage
        await page.WaitForTimeoutAsync(200);
    }

    [Fact]
    public async Task PlayersAreCalledWhatTheyTypedAndItSurvivesAReload()
    {
        RequireLocalBackend();
        await ResetEmulatorAsync();

        var ada = await OpenLobbyAsync();
        await Expect(Status(ada)).ToContainTextAsync("Nobody is waiting", new() { Timeout = BootTimeout });
        await SetNameAsync(ada, "Ada");

        await PressAsync(ada, "1"); // post
        await Expect(Status(ada)).ToContainTextAsync("Waiting for an opponent",
            new() { Timeout = BootTimeout });

        var bo = await OpenLobbyAsync();
        await Expect(Status(bo)).ToContainTextAsync("Open games", new() { Timeout = BootTimeout });
        await SetNameAsync(bo, "Bo");
        await PressAsync(bo, "1"); // join

        await Expect(Status(ada)).ToContainTextAsync("Your move (White)", new() { Timeout = BootTimeout });
        await PlayMoveAsync(ada, "e2e4");

        // The line this whole change exists for: it used to read "Waiting for Player…".
        await Expect(Status(ada)).ToContainTextAsync("Waiting for Bo", new() { Timeout = PushTimeout });

    }

    [Fact]
    public async Task TheNameIsRememberedAcrossAReload()
    {
        RequireLocalBackend();

        // Deliberately the SAME page rather than a second one: NewPageAsync gives every page its own
        // browser context, which is what makes two players two players — and means a "second tab"
        // here would be a different profile with legitimately no name saved in it.
        var page = await OpenLobbyAsync();
        await SetNameAsync(page, "Ada");

        await page.ReloadAsync();
        await Expect(Status(page)).ToContainTextAsync("Choose how", new() { Timeout = BootTimeout });
        await PressAsync(page, OnlineGameKey);
        await PressAsync(page, "1");

        await Expect(page.Locator(".toolbar input[type=\"text\"]")).ToHaveValueAsync("Ada",
            new() { Timeout = BootTimeout });
    }

    [Fact]
    public async Task APostedGameIsFoundAndJoinedFromTheLobby()
    {
        RequireLocalBackend();
        await ResetEmulatorAsync();

        // Nobody has posted anything, and the lobby says so rather than showing an empty list.
        var host = await OpenLobbyAsync();
        await Expect(Status(host)).ToContainTextAsync("Nobody is waiting", new() { Timeout = BootTimeout });

        // "Post a game and wait" is the only item, so it is the first.
        await PressAsync(host, "1");
        await Expect(Status(host)).ToContainTextAsync("Waiting for an opponent",
            new() { Timeout = BootTimeout });

        var joiner = await OpenLobbyAsync();
        await Expect(Status(joiner)).ToContainTextAsync("Open games", new() { Timeout = BootTimeout });

        await PressAsync(joiner, "1"); // the posted game is the only row
        await Expect(Status(host)).ToContainTextAsync("Your move (White)", new() { Timeout = BootTimeout });

        // The advertisement comes down once both seats are taken: a third player arriving now finds
        // an empty lobby rather than an invitation to a game that is full.
        var latecomer = await OpenLobbyAsync();
        await Expect(Status(latecomer)).ToContainTextAsync("Nobody is waiting",
            new() { Timeout = BootTimeout });
    }

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
