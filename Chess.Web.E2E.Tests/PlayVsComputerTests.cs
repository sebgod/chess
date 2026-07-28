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
    public async Task ChangingDifficultyMidGame_ReachesTheEngineAlreadyPlaying()
    {
        // The regression this exists for: the dropdown used to update only the field the NEXT game
        // reads, so it moved, the label changed, and the engine carried on at the old depth. Nothing
        // in the DOM says which move the engine picked — the board and move list are both painted into
        // the canvas — so the only externally visible consequence of search depth is how long the
        // search takes. Hence timing; but a RATIO between two measurements on this same page, never an
        // absolute threshold, so the test does not encode how fast the machine running it happens to be.
        // Two games rather than two moves of one game, so that the position being searched is held
        // constant and the dropdown is the only difference. Timing consecutive moves of a single game
        // instead would fold in how expensive each position happens to be, which is worth a factor of
        // ~3 by itself here — the same order as the effect being measured.
        var unchanged = await SecondReplyTimeAsync(raiseTo: null);
        var raised = await SecondReplyTimeAsync(raiseTo: "Normal");

        Assert.True(raised > unchanged * 3,
            $"Raising the level mid-game should make the very next search markedly longer, but it took " +
            $"{raised:F0} ms against {unchanged:F0} ms for the identical position left on Easy — the " +
            "change did not reach the engine that was already playing.");
    }

    // Plays 1.e4 (answered on Easy in both runs, so both games stand in the same position), optionally
    // changes the level, then plays 2.d4 and reports how long that reply took.
    //
    // Normal rather than Hard on purpose: one extra ply of depth is already a ~14x difference, which is
    // all the signal this needs, while depth 4 against a locally served — interpreted, non-AOT — build
    // measured 77 SECONDS for the single reply.
    private async Task<double> SecondReplyTimeAsync(string? raiseTo)
    {
        var page = await StartGameVsComputerAsync();
        await InstallStatusRecorderAsync(page);

        await MeasureThinkAsync(page, "e2e4");

        if (raiseTo is not null)
        {
            await page.Locator("select").SelectOptionAsync(raiseTo);
        }

        return await MeasureThinkAsync(page, "d2d4");
    }

    // Records every status change with a timestamp. A MutationObserver rather than polling from the
    // test, because the search blocks the single WASM thread: Playwright cannot read the DOM while
    // that runs, so an intermediate "thinking…" would be missed. The observer's callback is queued
    // before the block starts and the log is read back afterwards, which cannot miss it.
    private static async Task InstallStatusRecorderAsync(IPage page) => await page.EvaluateAsync("""
        () => {
            const el = document.querySelector('p.status');
            window.__chessStatus = [];
            new MutationObserver(() => window.__chessStatus.push(
                { t: performance.now(), s: el.textContent.trim() }))
                .observe(el, { childList: true, characterData: true, subtree: true });
        }
        """);

    // Plays a move and returns how long the engine's reply took, measured inside the page as the gap
    // between "thinking…" appearing and the status that replaces it once the search returns.
    private static async Task<double> MeasureThinkAsync(IPage page, string uci)
    {
        const string CompletedThink = """
            () => {
                const log = window.__chessStatus ?? [];
                const i = log.findLastIndex(e => e.s.includes('thinking'));
                return i >= 0 && i + 1 < log.length;
            }
            """;

        await page.EvaluateAsync("() => { window.__chessStatus = []; }");
        await PlayMoveAsync(page, uci);

        // Waiting on the recorder rather than on the status text: "White to move." is already showing
        // when the move is played, so waiting for it could pass before the engine has even started.
        await page.WaitForFunctionAsync(CompletedThink, null, new() { Timeout = 120_000 });

        return await page.EvaluateAsync<double>("""
            () => {
                const log = window.__chessStatus;
                const i = log.findLastIndex(e => e.s.includes('thinking'));
                return log[i + 1].t - log[i].t;
            }
            """);
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
