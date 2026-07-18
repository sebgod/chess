using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// Browser E2E for the Play-by-Link correspondence feature. The whole UI (wizard + board) is drawn
/// into a &lt;canvas&gt;, so these tests never read pixels — they assert on the observable surface
/// that unit tests can't reach: the address-bar fragment (written via history.replaceState), the
/// aria-live status paragraph, the real DOM share-row buttons, the help &lt;details&gt;, and the
/// clipboard. Moves are made through the desktop square-entry keymap ("e2e4" == keys e,2,e,4),
/// which needs no pixel math.
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class PlayByLinkTests(ChessWebFixture fixture)
{
    // WASM cold-boot (download runtime, load fonts, first frame) dwarfs any DOM settle time.
    private const float BootTimeout = 60_000;

    private ILocator Status(IPage page) => page.Locator("p.status");

    private async Task<IPage> OpenAsync(string fragment)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + fragment, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });
        return page;
    }

    // Enters a UCI move via the canvas keymap: focus the board, then press file/rank/file/rank.
    private static async Task PlayMoveAsync(IPage page, string uci)
    {
        await page.Locator("#board").FocusAsync();
        foreach (var ch in uci)
        {
            await page.Keyboard.PressAsync(ch.ToString());
            await page.WaitForTimeoutAsync(60); // let Blazor's async keydown handler settle
        }
    }

    private static Task WaitForHashAsync(IPage page, string hash) =>
        page.WaitForFunctionAsync("h => window.location.hash === h", hash,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

    // ── boot routing: wizard vs. straight-to-board ──────────────────────────

    [Fact]
    public async Task FreshVisit_ShowsWizard_NotAGame()
    {
        var page = await OpenAsync("");

        await Expect(Status(page)).ToContainTextAsync("Choose how you'd like to play",
            new() { Timeout = BootTimeout });

        // The board canvas is always present; the share row belongs to a live link game only.
        await Expect(page.Locator("#board")).ToBeVisibleAsync();
        await Expect(page.Locator("button.share.copy")).ToHaveCountAsync(0);
        // The explainer is discoverable but collapsed off the menu (it springs open in a game).
        await Expect(page.Locator("details.help")).Not.ToHaveAttributeAsync("open", "");
    }

    [Fact]
    public async Task InviteLink_EmptyGame_OpensAsWhiteToMove()
    {
        // "#g=" is the invitation a Black-playing creator sends: the recipient opens it, plays White.
        var page = await OpenAsync("#g=");

        await Expect(Status(page)).ToContainTextAsync("Your move (White)",
            new() { Timeout = BootTimeout });
        // A link game opens the explainer automatically, right where the sender first needs it.
        await Expect(page.Locator("details.help")).ToHaveAttributeAsync("open", "");
    }

    [Fact]
    public async Task GameLink_MidGame_OpensAsSideToMove_NoShareRowYet()
    {
        // One ply played → Black to move → the opener controls Black. It's genuinely their turn,
        // so nothing is staged to share yet: no share row until a move is made.
        var page = await OpenAsync("#g=e2e4");

        await Expect(Status(page)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = BootTimeout });
        await Expect(page.Locator("button.share.copy")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task InvalidLink_ShowsError_AndFallsBackToMenu()
    {
        // e2e9 — rank out of range. The rules-engine replay rejects it; the app must surface the
        // error rather than crash or silently mis-play.
        var page = await OpenAsync("#g=e2e9");

        await Expect(Status(page)).ToContainTextAsync("isn't a valid chess game",
            new() { Timeout = BootTimeout });
        await Expect(page.Locator("#board")).ToBeVisibleAsync();
    }

    // ── making a move: the handoff ──────────────────────────────────────────

    [Fact]
    public async Task MakingMove_UpdatesFragment_AndRevealsShareRow()
    {
        var page = await OpenAsync("#g=e2e4");
        await Expect(Status(page)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = BootTimeout });

        await PlayMoveAsync(page, "e7e5");

        // The address bar now holds the appended move (history.replaceState) — a bookmark here is
        // the saved game.
        await WaitForHashAsync(page, "#g=e2e4.e7e5");
        // Turn handed over → the share affordances appear, status prompts the send.
        await Expect(page.Locator("button.share.copy")).ToBeVisibleAsync();
        await Expect(page.Locator("a.share.mail")).ToBeVisibleAsync();
        await Expect(Status(page)).ToContainTextAsync("Move staged");
    }

    [Fact]
    public async Task CopyLink_WritesCanonicalUrlToClipboard()
    {
        var page = await OpenAsync("#g=e2e4");
        await Expect(Status(page)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = BootTimeout });
        await PlayMoveAsync(page, "e7e5");
        await WaitForHashAsync(page, "#g=e2e4.e7e5");

        await page.BringToFrontAsync(); // clipboard access needs the document focused
        await page.Locator("button.share.copy").ClickAsync();

        // Transient confirmation label, then the clipboard holds the full canonical game URL.
        await Expect(page.Locator("button.share.copy")).ToContainTextAsync("Copied");
        var clip = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.Equal(fixture.BaseUrl + "#g=e2e4.e7e5", clip);
    }

    [Fact]
    public async Task SecondLinkInSameTab_AppliesWithoutReload()
    {
        var page = await OpenAsync("#g=e2e4");
        await Expect(Status(page)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = BootTimeout });

        // Marker survives a same-document navigation but not a full reload — this is how we prove
        // the opponent's reply link (pasted into the same tab) is applied without reloading WASM.
        await page.EvaluateAsync("() => { window.__noReload = true; }");

        // Opponent replied: Black's e7e5, then White's g1f3 → back to Black to move.
        await page.EvaluateAsync("() => { window.location.hash = 'g=e2e4.e7e5.g1f3'; }");

        await Expect(Status(page)).ToContainTextAsync("Your move (Black)");
        await WaitForHashAsync(page, "#g=e2e4.e7e5.g1f3");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__noReload === true"),
            "the app reloaded instead of applying the fragment in place");
    }

    [Fact]
    public async Task Undo_RevertsToReceivedState_AndHidesShareRow()
    {
        var page = await OpenAsync("#g=e2e4");
        await Expect(Status(page)).ToContainTextAsync("Your move (Black)",
            new() { Timeout = BootTimeout });

        await PlayMoveAsync(page, "e7e5");
        await WaitForHashAsync(page, "#g=e2e4.e7e5");
        await Expect(page.Locator("button.share.undo")).ToBeVisibleAsync();

        await page.Locator("button.share.undo").ClickAsync();

        // Back to the position as received: Black to move, address bar restored, share row gone.
        await WaitForHashAsync(page, "#g=e2e4");
        await Expect(Status(page)).ToContainTextAsync("Your move (Black)");
        await Expect(page.Locator("button.share.copy")).ToHaveCountAsync(0);
    }
}
