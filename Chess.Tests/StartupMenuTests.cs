using Chess.Console;
using Chess.Lib;
using Console.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public class StartupMenuTests
{
    private sealed class FakeTerminal(Queue<ConsoleInputEvent> inputs) : IVirtualTerminal
    {
        public bool IsAlternateScreen => true;
        public bool HasInput() => inputs.Count > 0;
        public ConsoleInputEvent TryReadInput() => inputs.Dequeue();
        public Task<bool> HasSixelSupportAsync() => Task.FromResult(false);
        public Task<(uint Width, uint Height)?> QueryCellSizeAsync() => Task.FromResult<(uint, uint)?>(null);
        public ValueTask EnterAlternateScreenAsync() => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(DataSource))]
    public async Task ShowAsync_ReturnsExpectedResult(
        Queue<ConsoleInputEvent> inputs,
        GameMode expectedMode,
        Side expectedComputerSide)
    {
        var terminal = new FakeTerminal(inputs);
        var menu = new StartupMenu(terminal, TimeProvider.System);

        var (mode, computerSide) = await menu.ShowAsync(CancellationToken.None);

        mode.ShouldBe(expectedMode);
        computerSide.ShouldBe(expectedComputerSide);
    }

    public static IEnumerable<object[]> DataSource() =>
    [
        ArrowNavigation(),
        DigitShortcut(),
        MouseClick(),
        FullPvP(),
        FullPvCWhite(),
        FullCustomGame(),
    ];

    private static ConsoleInputEvent Key(ConsoleKey key) => new(null, key, 0);

    private static ConsoleInputEvent Mouse(int y) =>
        new(new MouseEvent(0, 0, y, IsRelease: true), ConsoleKey.None, 0);

    /// <summary>
    /// Computes the row of the first menu item, matching DrawMenuAlternateScreen logic.
    /// </summary>
    private static int ComputeMenuStartRow(int itemCount)
    {
        int windowHeight;
        try { windowHeight = System.Console.WindowHeight; }
        catch (IOException) { windowHeight = 0; }
        var totalLines = 4 + itemCount;
        var startRow = Math.Max(0, (windowHeight - totalLines) / 2);
        return startRow + 4;
    }

    // Arrow key navigation + Enter: DownArrow moves to PvC, Enter confirms, D1 selects White
    private static object[] ArrowNavigation()
    {
        var inputs = new Queue<ConsoleInputEvent>([
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.D1),
        ]);
        return [inputs, GameMode.PlayerVsComputer, Side.Black];
    }

    // Digit shortcut: D1 selects item 0 (Player vs Player)
    private static object[] DigitShortcut()
    {
        var inputs = new Queue<ConsoleInputEvent>([
            Key(ConsoleKey.D1),
        ]);
        return [inputs, GameMode.PlayerVsPlayer, Side.None];
    }

    // Mouse click on first menu item selects PvP
    private static object[] MouseClick()
    {
        var menuStartRow = ComputeMenuStartRow(3);
        var inputs = new Queue<ConsoleInputEvent>([
            Mouse(menuStartRow),
        ]);
        return [inputs, GameMode.PlayerVsPlayer, Side.None];
    }

    // Full PvP flow: D1 selects PvP
    private static object[] FullPvP()
    {
        var inputs = new Queue<ConsoleInputEvent>([
            Key(ConsoleKey.D1),
        ]);
        return [inputs, GameMode.PlayerVsPlayer, Side.None];
    }

    // Full PvC White flow: D2 selects PvC, D1 selects White
    private static object[] FullPvCWhite()
    {
        var inputs = new Queue<ConsoleInputEvent>([
            Key(ConsoleKey.D2),
            Key(ConsoleKey.D1),
        ]);
        return [inputs, GameMode.PlayerVsComputer, Side.Black];
    }

    // Full Custom Game flow: D3 selects Custom, D1 selects Empty, D2 selects Black
    private static object[] FullCustomGame()
    {
        var inputs = new Queue<ConsoleInputEvent>([
            Key(ConsoleKey.D3),
            Key(ConsoleKey.D1),
            Key(ConsoleKey.D2),
        ]);
        return [inputs, GameMode.CustomGameEmpty, Side.White];
    }
}
