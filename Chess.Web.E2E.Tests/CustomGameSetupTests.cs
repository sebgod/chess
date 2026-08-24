using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// Browser E2E for the custom-game setup flow: arrange a board, press Start, play it.
///
/// <para>Setup now runs through the shared <c>GameSession</c>'s setup phase rather than a screen of
/// its own, which means the transition out of it — freezing the arranged position into a real game and
/// bringing the engine up against it — happens in library code the front-end only reacts to. That
/// handoff is exactly the sort of thing unit tests can assert in isolation and still get wrong in a
/// browser, so it gets covered here.</para>
///
/// <para>The board is a canvas, so these tests assert on the DOM surface only: which control buttons
/// are present (the Start button belongs to setup alone) and the aria-live status paragraph.</para>
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class CustomGameSetupTests(ChessWebFixture fixture)
{
    // WASM cold-boot (download runtime, load fonts, first frame) dwarfs any DOM settle time.
    private const float BootTimeout = 60_000;

    private static ILocator Status(IPage page) => page.Locator(".status");
    private static ILocator StartButton(IPage page) => page.Locator("button.new", new() { HasTextString = "Start game" });

    private static async Task PressAsync(IPage page, string key)
    {
        await page.Keyboard.PressAsync(key);
        await page.WaitForTimeoutAsync(120); // let Blazor's async keydown handler settle
    }

    // Walks the wizard into a custom game: Custom Game -> Standard Board -> White moves first ->
    // play as White -> Easy. Digits select-and-confirm, so each press is one whole step.
    private async Task<IPage> EnterSetupAsync()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Status(page)).ToContainTextAsync("Choose how you'd like to play",
            new() { Timeout = BootTimeout });

        await page.Locator("#board").FocusAsync();
        await PressAsync(page, "3"); // Custom Game
        await PressAsync(page, "2"); // Standard Board
        await PressAsync(page, "1"); // White moves first
        await PressAsync(page, "1"); // play as White
        await PressAsync(page, "1"); // Easy

        await Expect(Status(page)).ToContainTextAsync("Set up the board", new() { Timeout = 15_000 });
        return page;
    }

    [Fact]
    public async Task CustomGame_OpensInSetup()
    {
        var page = await EnterSetupAsync();

        // The Start button exists only while setting up — its presence is the screen.
        await Expect(StartButton(page)).ToBeVisibleAsync();
    }

    /// <summary>
    /// The setup hint names the gesture that now exists. Asserting on "drag" rather than only on
    /// the "Set up the board" prefix is deliberate: the prefix is in BOTH the old and new wording,
    /// so a dev server serving a stale build would pass it — the exact false pass that cost tianwen
    /// two debugging sessions (see its 39df8fb8). This assertion can only pass against a build that
    /// has the feature.
    /// </summary>
    [Fact]
    public async Task Setup_TellsYouThePieceCanBeDragged()
    {
        var page = await EnterSetupAsync();

        await Expect(Status(page)).ToContainTextAsync("drag a piece");
    }

    /// <summary>
    /// Relocation on the web, driven by the square-entry keymap so no pixel math is involved: g1
    /// takes the knight into hand — which the status says, and saying it IS the feedback on this
    /// front-end — and f3 puts it down. Then g1 reports as empty by opening the picker instead of
    /// picking anything up, which is what proves the knight actually left rather than being copied.
    /// </summary>
    [Fact]
    public async Task Setup_APieceCanBePickedUpAndDroppedOnAnotherSquare()
    {
        var page = await EnterSetupAsync();

        await PressAsync(page, "g");
        await PressAsync(page, "1");
        await Expect(Status(page)).ToContainTextAsync("moving White Knight from g1");

        await PressAsync(page, "f");
        await PressAsync(page, "3");
        await Expect(Status(page)).ToContainTextAsync("Set up the board");

        // f3 now holds it...
        await PressAsync(page, "f");
        await PressAsync(page, "3");
        await Expect(Status(page)).ToContainTextAsync("moving White Knight from f3");

        // ...and g1 does not: an empty square opens the picker, which takes nothing into hand.
        await PressAsync(page, "Escape");
        await PressAsync(page, "g");
        await PressAsync(page, "1");
        await Expect(Status(page)).ToContainTextAsync("Set up the board");
    }

    [Fact]
    public async Task StartButton_LeavesSetupAndBeginsPlay()
    {
        // The handoff the session now owns: lower the setup flag, and the next tick freezes the board
        // into a real game. If that transition were missed the page would sit in setup for ever.
        var page = await EnterSetupAsync();

        await StartButton(page).ClickAsync();

        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });
        await Expect(StartButton(page)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AfterStarting_TheGameIsPlayableAndTheEngineAnswers()
    {
        // The engine is created against the arranged position, not the one the wizard started from,
        // so proving it replies here proves the whole setup -> StartAsync -> play chain.
        var page = await EnterSetupAsync();
        await StartButton(page).ClickAsync();
        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });

        await page.Locator("#board").FocusAsync();
        foreach (var ch in "e2e4")
        {
            await PressAsync(page, ch.ToString());
        }

        // Back round to White means both plies landed — ours and the engine's.
        await Expect(Status(page)).ToContainTextAsync("White to move.", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task SettingUp_DoesNotLetTheEngineJumpTheGun()
    {
        // A half-built position is not one to move in: Game.SetPiece throws once a ply exists, so an
        // engine move during setup would lock the board being arranged.
        var page = await EnterSetupAsync();

        // Clicks land on the board while setup is open; none of them may start a game.
        await page.Locator("#board").ClickAsync(new() { Position = new Position { X = 200, Y = 200 } });
        await page.WaitForTimeoutAsync(500);

        await Expect(StartButton(page)).ToBeVisibleAsync();
        await Expect(Status(page)).ToContainTextAsync("Set up the board");
    }
}
