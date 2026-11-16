using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// The menu is a panel over the board (#84): Esc and Backspace step the wizard back, as the "Back" row does. Taken
/// by the page before the menu's router, whose modal panel would otherwise swallow them.
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class MenuPanelTests(ChessWebFixture fixture)
{
    private const float BootTimeout = 60_000;

    private static ILocator Status(IPage page) => page.Locator(".status");

    private static async Task PressAsync(IPage page, string key)
    {
        await page.Keyboard.PressAsync(key);
        await page.WaitForTimeoutAsync(120); // let Blazor's async keydown handler settle
    }

    [Theory]
    [InlineData("Escape")]
    [InlineData("Backspace")]
    public async Task A_back_key_returns_to_the_first_step(string backKey)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Status(page)).ToContainTextAsync("Choose how you'd like to play", new() { Timeout = BootTimeout });

        // Player vs Computer, then back. Had the key not gone back, "3" would pick a colour on the next step instead
        // of Custom Game, and setup never opens.
        await PressAsync(page, "2");
        if (Environment.GetEnvironmentVariable("CHESS_E2E_SHOTS") is { Length: > 0 } shots)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"menu-panel-{backKey}.png") });
        }
        await PressAsync(page, backKey);

        await PressAsync(page, "3"); // Custom Game
        await PressAsync(page, "2"); // Standard Board
        await PressAsync(page, "1");
        await PressAsync(page, "1");
        await PressAsync(page, "1");

        await Expect(Status(page)).ToContainTextAsync("Set up the board", new() { Timeout = 15_000 });
    }
}
