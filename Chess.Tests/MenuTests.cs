using Console.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public class MenuTests
{
    private sealed class TestMenu(
        IVirtualTerminal terminal, TimeProvider timeProvider,
        string title, string prompt, string[] items)
        : MenuBase<int>(terminal, timeProvider)
    {
        protected override Task<int> ShowAsyncCore(CancellationToken cancellationToken)
            => ShowMenuAsync(title, prompt, items, cancellationToken);
    }

    private static readonly string[] TestItems = ["Alpha", "Beta", "Gamma"];

    // --- Non-alternate screen tests (digit shortcuts only) ---

    [Theory]
    [MemberData(nameof(DigitDataSource))]
    public async Task ShowAsync_NonAlternate_DigitSelectsItem(
        Queue<ConsoleInputEvent> inputs,
        int expectedIndex)
    {
        var terminal = new FakeTerminal(inputs);
        var menu = new TestMenu(terminal, TimeProvider.System, "Title", "Pick one:", TestItems);

        var result = await menu.ShowAsync(CancellationToken.None);

        terminal.IsAlternateScreen.ShouldBeFalse();
        result.ShouldBe(expectedIndex);
    }

    public static IEnumerable<object[]> DigitDataSource() =>
    [
        [new Queue<ConsoleInputEvent>([Key(ConsoleKey.D1)]), 0],
        [new Queue<ConsoleInputEvent>([Key(ConsoleKey.D2)]), 1],
        [new Queue<ConsoleInputEvent>([Key(ConsoleKey.D3)]), 2],
    ];

    // --- Alternate screen tests (arrow keys, Enter, mouse) ---

    [Theory]
    [MemberData(nameof(AlternateDataSource))]
    public async Task ShowAsync_Alternate_ReturnsExpectedIndex(
        Queue<ConsoleInputEvent> inputs,
        int expectedIndex)
    {
        var terminal = new FakeTerminal(inputs);
        terminal.EnterAlternateScreen();
        var menu = new TestMenu(terminal, TimeProvider.System, "Title", "Pick one:", TestItems);

        var result = await menu.ShowAsync(CancellationToken.None);

        terminal.IsAlternateScreen.ShouldBeTrue();
        result.ShouldBe(expectedIndex);
    }

    public static IEnumerable<object[]> AlternateDataSource() =>
    [
        ArrowDownThenEnter(),
        ArrowUpWrapsThenEnter(),
        DigitShortcutInAlternate(),
        MouseClickOnItem(),
    ];

    private static ConsoleInputEvent Key(ConsoleKey key) => new(null, key, 0);

    private static ConsoleInputEvent Mouse(int y) =>
        new(new MouseEvent(0, 0, y, IsRelease: true), ConsoleKey.None, 0);

    /// <summary>
    /// Computes the row of the first menu item, matching DrawMenuAlternateScreen logic.
    /// Uses the same default height (24) as <see cref="FakeTerminal"/>.
    /// </summary>
    private static int ComputeMenuStartRow(int itemCount, int windowHeight = 24)
    {
        var totalLines = 4 + itemCount;
        var startRow = Math.Max(0, (windowHeight - totalLines) / 2);
        return startRow + 4;
    }

    // DownArrow moves to index 1, Enter confirms
    private static object[] ArrowDownThenEnter() =>
    [
        new Queue<ConsoleInputEvent>([Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]),
        1,
    ];

    // UpArrow from index 0 wraps to last item (index 2), Enter confirms
    private static object[] ArrowUpWrapsThenEnter() =>
    [
        new Queue<ConsoleInputEvent>([Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]),
        2,
    ];

    // D1 selects index 0 in alternate screen too
    private static object[] DigitShortcutInAlternate() =>
    [
        new Queue<ConsoleInputEvent>([Key(ConsoleKey.D1)]),
        0,
    ];

    // Mouse click on first menu item row selects index 0
    // FakeTerminal.CellSize is (10, 20), so mouse events use pixel coordinates
    private static object[] MouseClickOnItem()
    {
        const int cellHeight = 20;
        var menuStartRow = ComputeMenuStartRow(3);
        return
        [
            new Queue<ConsoleInputEvent>([Mouse(menuStartRow * cellHeight)]),
            0,
        ];
    }
}
