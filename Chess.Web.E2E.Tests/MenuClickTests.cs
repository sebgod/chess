using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// The startup menu takes a mouse click, not just the keyboard. Since DIR.Lib 10 the menu widget's own
/// <c>HandleInput</c> takes keys only and a click reaches a row through an <c>InputRouter</c>; the page handed
/// clicks straight to the widget, so they were dropped while every keyboard test here still passed.
/// </summary>
[Collection(ChessWebCollection.Name)]
public sealed class MenuClickTests(ChessWebFixture fixture)
{
    private const float BootTimeout = 60_000;

    // Measured off the rendered first menu step at the fixture's 1280x720 viewport, in page coordinates. The rows
    // are 54px apart and the list is centred on y 458, so where a row sits depends on how many there are: four
    // (Player vs Player, Player vs Computer, Custom Game, Play by Link) on a server with no backend config, five
    // when there is one and "Online game" is appended (CI's e2e job writes one for the emulator). A fresh browser
    // has no saved game, so there is never a "Continue game" row.
    private const int MenuCentreX = 640;
    private const int ListCentreY = 458;
    private const int RowPitch = 54;
    private const int CustomGameIndex = 2;

    private static ILocator Status(IPage page) => page.Locator(".status");

    private static async Task PressAsync(IPage page, string key)
    {
        await page.Keyboard.PressAsync(key);
        await page.WaitForTimeoutAsync(120); // let Blazor's async keydown handler settle
    }

    // The page shows a "Play online" chip exactly when the menu has its "Online game" row.
    private static async Task<int> RowYAsync(IPage page, int index)
    {
        var online = await page.Locator("button.chip", new() { HasTextString = "Play online" }).CountAsync() > 0;
        var rows = online ? 5 : 4;
        return ListCentreY + (int)(RowPitch * (index - (rows - 1) / 2.0));
    }

    [Fact]
    public async Task Clicking_a_menu_row_confirms_it()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Status(page)).ToContainTextAsync("Choose how you'd like to play", new() { Timeout = BootTimeout });

        await page.Mouse.ClickAsync(MenuCentreX, await RowYAsync(page, CustomGameIndex)); // Custom Game, by mouse
        await page.WaitForTimeoutAsync(200);

        // The rest of the custom-game wizard by keyboard: Standard Board, White first, play White, Easy. Had the
        // click been dropped, "2" would pick Player vs Computer on the first step instead, and setup never opens.
        await PressAsync(page, "2");
        await PressAsync(page, "1");
        await PressAsync(page, "1");
        await PressAsync(page, "1");

        await Expect(Status(page)).ToContainTextAsync("Set up the board", new() { Timeout = 15_000 });
    }
}
