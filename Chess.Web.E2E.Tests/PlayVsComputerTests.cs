using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// Browser E2E for playing the in-process engine.
///
/// <para>This path is worth covering separately from Play-by-Link because it is the one that has to
/// interleave a <em>blocking</em> search with painting: WASM is single-threaded, so the "thinking…"
/// frame has to reach the screen before the search starts or the tab simply freezes on stale pixels.
/// Everything else about the turn model is unit-tested against <c>GameSession</c>; only the
/// paint-then-block sequencing needs a real browser to be believed.</para>
///
/// <para>Like the link tests, nothing here reads pixels — the side to move in the aria-live status
/// paragraph is enough to tell whether the engine actually replied.</para>
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class PlayVsComputerTests(ChessWebFixture fixture)
{
    // WASM cold-boot (download runtime, load fonts, first frame) dwarfs any DOM settle time.
    private const float BootTimeout = 60_000;

    private static ILocator Status(IPage page) => page.Locator("p.status");

    // Walks the startup wizard to a Player-vs-Computer game as White against the weakest engine.
    // Digits select-and-confirm in the menu, so each press is one whole step.
    private async Task<IPage> StartGameVsComputerAsync()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Status(page)).ToContainTextAsync("Choose how you'd like to play",
            new() { Timeout = BootTimeout });

        await page.Locator("#board").FocusAsync();
        await PressAsync(page, "2"); // Player vs Computer
        await PressAsync(page, "1"); // play as White
        await PressAsync(page, "1"); // Easy — keep the search short

        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 15_000 });
        return page;
    }

    private static async Task PressAsync(IPage page, string key)
    {
        await page.Keyboard.PressAsync(key);
        await page.WaitForTimeoutAsync(120); // let Blazor's async keydown handler settle
    }

    // Enters a UCI move via the canvas keymap: file/rank/file/rank.
    private static async Task PlayMoveAsync(IPage page, string uci)
    {
        await page.Locator("#board").FocusAsync();
        foreach (var ch in uci)
        {
            await PressAsync(page, ch.ToString());
        }
    }

    [Fact]
    public async Task HumanMove_IsAnsweredByTheEngine()
    {
        var page = await StartGameVsComputerAsync();

        await PlayMoveAsync(page, "e2e4");

        // The discriminator: if the engine had not replied it would still be Black to move. Coming
        // back round to White is the whole turn — human ply, engine ply — having been advanced.
        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task SeveralMovesInARow_KeepAlternatingBackToUs()
    {
        // Guards the tick loop specifically: each human move must advance exactly one engine reply and
        // then stop, not stall on the second move and not run away advancing both sides.
        var page = await StartGameVsComputerAsync();

        foreach (var move in new[] { "e2e4", "d2d4", "g1f3" })
        {
            await PlayMoveAsync(page, move);
            await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });
        }
    }

    [Fact]
    public async Task TheBoardStaysInteractive_AfterTheEngineHasMoved()
    {
        // The engine's search sets the busy gate that disables input. If it were ever left set, the
        // game would look alive but refuse every subsequent move — so play on after a reply.
        var page = await StartGameVsComputerAsync();
        await PlayMoveAsync(page, "e2e4");
        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });

        await PlayMoveAsync(page, "d2d4");

        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });
        await Expect(page.Locator("button.new")).ToBeEnabledAsync();
    }
}
