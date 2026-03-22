using System.Text;
using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Chess.Console.Tests;

/// <summary>
/// Integration tests for non-alternate-screen (ASCII) mode, exercising the full
/// GameLoop → HumanPlayer → AsciiDisplay pipeline with queued keyboard input.
/// </summary>
public class AsciiGameIntegrationTests
{
    /// <summary>
    /// Plays Fool's mate (1. f3 e5 2. g4 Qh4#) through the full game loop
    /// and verifies that checkmate is rendered.
    /// </summary>
    [Fact]
    public async Task FoolsMate_PvP_RendersCheckmate()
    {
        // Arrange: queue menu selection (PvP) + moves for Fool's mate
        var inputs = new Queue<ConsoleInputEvent>(
        [
            // Select "Player vs Player" in menu
            Key(ConsoleKey.D1),

            // 1. f3: select f2, move to f3
            Key(ConsoleKey.F), Key(ConsoleKey.D2),
            Key(ConsoleKey.F), Key(ConsoleKey.D3),

            // 1... e5: select e7, move to e5
            Key(ConsoleKey.E), Key(ConsoleKey.D7),
            Key(ConsoleKey.E), Key(ConsoleKey.D5),

            // 2. g4: select g2, move to g4
            Key(ConsoleKey.G), Key(ConsoleKey.D2),
            Key(ConsoleKey.G), Key(ConsoleKey.D4),

            // 2... Qh4#: select d8, move to h4
            Key(ConsoleKey.D), Key(ConsoleKey.D8),
            Key(ConsoleKey.H), Key(ConsoleKey.D4),
        ]);

        var terminal = new TestableTerminal(inputs);
        var timeProvider = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();

        var menu = new StartupMenu(terminal, timeProvider);
        var (gameMode, computerSide, _) = await menu.ShowAsync(cts.Token);

        gameMode.ShouldBe(GameMode.PlayerVsPlayer);
        computerSide.ShouldBe(Side.None);

        var gameLoop = new GameLoop(
            timeProvider,
            () => new AsciiDisplay(terminal),
            () => new HumanPlayer(terminal),
            (_, _) => throw new InvalidOperationException("No engine in PvP"));

        // The loop exits when it runs out of input (HasInput returns false),
        // so we cancel after a short time to prevent infinite idle polling.
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // Need to advance time so Task.Delay in the game loop resolves
        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(20));
                await Task.Delay(1);
            }
        }, cts.Token);

        await gameLoop.RunAsync(gameMode, computerSide, Side.White, cts.Token);

        // Assert: output should contain checkmate
        var output = terminal.Output;
        output.ShouldContain("Checkmate");
    }

    /// <summary>
    /// Verifies that the menu shows numbered items and the prompt in non-alternate mode.
    /// </summary>
    [Fact]
    public async Task Menu_PvP_ShowsNumberedItemsAndPrompt()
    {
        var inputs = new Queue<ConsoleInputEvent>([Key(ConsoleKey.D1)]);
        var terminal = new TestableTerminal(inputs);
        var menu = new StartupMenu(terminal, new FakeTimeProvider());

        await menu.ShowAsync(CancellationToken.None);

        var output = terminal.Output;
        output.ShouldContain("1) Player vs Player");
        output.ShouldContain("2) Player vs Computer");
        output.ShouldContain("3) Custom Game");
        output.ShouldContain("> ");
    }

    /// <summary>
    /// Verifies that pressing a file key shows pending file in the prompt.
    /// </summary>
    [Fact]
    public void PendingFile_ShowsInPrompt()
    {
        var inputs = new Queue<ConsoleInputEvent>([Key(ConsoleKey.E)]);
        var terminal = new TestableTerminal(inputs);
        var display = new AsciiDisplay(terminal);
        var game = new Game();
        display.ResetGame(game);

        var player = new HumanPlayer(terminal);
        var result = player.TryMakeMove(display.UI);

        result.ShouldNotBeNull();
        display.UI.PendingFile.ShouldBe(Chess.Lib.File.E);

        display.RenderMove(game, result.Value.Response, result.Value.ClipRects);

        terminal.Output.ShouldContain("> e");
    }

    /// <summary>
    /// Verifies that selecting a piece shows the selected square in the prompt.
    /// </summary>
    [Fact]
    public void SelectedPiece_ShowsInPrompt()
    {
        // Select e2 (white pawn)
        var inputs = new Queue<ConsoleInputEvent>(
        [
            Key(ConsoleKey.E), Key(ConsoleKey.D2),
        ]);
        var terminal = new TestableTerminal(inputs);
        var display = new AsciiDisplay(terminal);
        var game = new Game();
        display.ResetGame(game);

        var player = new HumanPlayer(terminal);

        // First: file 'e' (pending)
        player.TryMakeMove(display.UI);
        // Second: rank '2' (completes selection of e2)
        var result = player.TryMakeMove(display.UI);

        result.ShouldNotBeNull();
        display.RenderMove(game, result.Value.Response, result.Value.ClipRects);

        terminal.Output.ShouldContain("[e2]");
    }

    /// <summary>
    /// Verifies that after checkmate, no prompt is shown.
    /// </summary>
    [Fact]
    public void Checkmate_NoPromptShown()
    {
        var terminal = new TestableTerminal(new Queue<ConsoleInputEvent>());
        var display = new AsciiDisplay(terminal);
        var game = new Game();
        display.ResetGame(game);

        // Play Fool's mate directly on the game object
        game.TryMove(Chess.Lib.Action.DoMove(Position.F2, Position.F3));
        game.TryMove(Chess.Lib.Action.DoMove(Position.E7, Position.E5));
        game.TryMove(Chess.Lib.Action.DoMove(Position.G2, Position.G4));
        game.TryMove(Chess.Lib.Action.DoMove(Position.D8, Position.H4));

        game.GameStatus.ShouldBe(GameStatus.Checkmate);

        terminal.ClearOutput();
        display.RenderMove(game, UIResponse.NeedsRefresh, []);

        var output = terminal.Output;
        output.ShouldContain("Checkmate");
        output.ShouldNotContain("\r> ");
    }

    /// <summary>
    /// Verifies that invalid keys (not a-h or 1-8) are silently ignored.
    /// </summary>
    [Fact]
    public void InvalidKey_IsIgnored()
    {
        var inputs = new Queue<ConsoleInputEvent>([Key(ConsoleKey.Z)]);
        var terminal = new TestableTerminal(inputs);
        var display = new AsciiDisplay(terminal);
        var game = new Game();
        display.ResetGame(game);

        var player = new HumanPlayer(terminal);
        var result = player.TryMakeMove(display.UI);

        result.ShouldNotBeNull();
        display.UI.PendingFile.ShouldBeNull();
        result.Value.Response.ShouldBe(UIResponse.None);
    }

    private static ConsoleInputEvent Key(ConsoleKey key) => new(null, key, 0);

    /// <summary>
    /// A fake terminal that queues input events and captures output,
    /// for testing the full ASCII-mode pipeline without a real console.
    /// </summary>
    private sealed class TestableTerminal(Queue<ConsoleInputEvent> inputs) : IVirtualTerminal
    {
        private readonly StringBuilder _output = new();

        public string Output => _output.ToString();
        public void ClearOutput() => _output.Clear();

        // ITerminalViewport
        public (int Column, int Row) Offset => (0, 0);
        public (int Width, int Height) Size => (80, 24);
        public TermCell CellSize => new(10, 20);
        public ColorMode ColorMode => ColorMode.Sgr16;
        public void SetCursorPosition(int left, int top) { }
        public void Write(string text) => _output.Append(text);
        public void WriteLine(string? text = null) { _output.Append(text); _output.Append('\n'); }
        public void Flush() { }
        public Stream OutputStream => Stream.Null;

        // IVirtualTerminal
        public Task InitAsync() => Task.CompletedTask;
        public bool HasSixelSupport => false;
        public bool HasColorSupport => false;
        public bool IsInputRedirected => true;
        public bool IsOutputRedirected => false;
        public void EnterAlternateScreen() { }
        public bool IsAlternateScreen => false;
        public void Clear() => _output.Clear();
        public bool HasInput() => inputs.Count > 0;
        public ConsoleInputEvent TryReadInput() => inputs.Dequeue();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
