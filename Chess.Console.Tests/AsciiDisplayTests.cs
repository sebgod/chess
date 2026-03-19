using System.Collections.Immutable;
using System.Text;
using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.Position;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.Console.Tests;

public class AsciiDisplayTests
{
    private sealed class FakeTerminal : IVirtualTerminal
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
        public void EnterAlternateScreen() { }
        public bool IsAlternateScreen => false;
        public void Clear() => _output.Clear();
        public bool HasInput() => false;
        public ConsoleInputEvent TryReadInput() => default;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static (AsciiDisplay Display, FakeTerminal Terminal) CreateDisplay()
    {
        var terminal = new FakeTerminal();
        var display = new AsciiDisplay(terminal);
        return (display, terminal);
    }

    [Fact]
    public void ResetGame_InitializesUI()
    {
        var (display, _) = CreateDisplay();
        var game = new Game();

        display.ResetGame(game);

        display.UI.ShouldNotBeNull();
        display.UI.Game.ShouldBe(game);
    }

    [Fact]
    public void ResetGame_ThrowsBeforeInit()
    {
        var (display, _) = CreateDisplay();

        Should.Throw<InvalidOperationException>(() => _ = display.UI);
    }

    [Fact]
    public void RenderInitial_OutputsStandardBoard()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        display.RenderInitial(game);

        var output = terminal.Output;

        // Rank labels 8 down to 1
        for (var rank = 8; rank >= 1; rank--)
            output.ShouldContain($" {rank}  ");

        // File labels a through h
        output.ShouldContain(" a");
        output.ShouldContain(" h");

        // Standard opening position pieces — rank 8 (black back rank)
        output.ShouldContain("r");
        output.ShouldContain("n");
        output.ShouldContain("b");
        output.ShouldContain("q");
        output.ShouldContain("k");

        // White pieces (uppercase)
        output.ShouldContain("R");
        output.ShouldContain("N");
        output.ShouldContain("B");
        output.ShouldContain("Q");
        output.ShouldContain("K");
        output.ShouldContain("P");
    }

    [Fact]
    public void RenderInitial_ShowsWhiteToMoveStatus()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        display.RenderInitial(game);

        terminal.Output.ShouldContain("White to move.");
    }

    [Fact]
    public void RenderBoard_SkipsDuplicateRender()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        display.RenderInitial(game);
        var firstLength = terminal.Output.Length;

        display.RenderInitial(game);

        // Second render should add minimal output (just a newline from RenderBoard early-return path)
        // since FEN hasn't changed
        terminal.Output.Length.ShouldBeLessThan(firstLength * 2);
    }

    [Fact]
    public void RenderMove_WithNeedsRefresh_RendersBoard()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        display.RenderMove(game, UIResponse.NeedsRefresh, [], null);

        terminal.Output.ShouldContain("White to move.");
    }

    [Fact]
    public void RenderMove_WithIsUpdate_RendersBoard()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        display.RenderMove(game, UIResponse.IsUpdate, [], null);

        terminal.Output.ShouldContain("White to move.");
    }

    [Fact]
    public void RenderMove_WithNoFlags_ShowsPromptOnly()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        display.RenderMove(game, UIResponse.None, [], null);

        terminal.Output.ShouldContain("> ");
    }

    [Fact]
    public void RenderMove_AfterMove_ShowsPGN()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        game.TryMove(DoMove(E2, E4));

        display.RenderMove(game, UIResponse.NeedsRefresh, [], null);

        terminal.Output.ShouldContain("e4");
    }

    [Fact]
    public void RenderMove_Keymap_ShowsMarkdownControls()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);
        display.UI.ShowingKeymap = true;

        display.RenderMove(game, UIResponse.NeedsRefresh, [], null);

        var output = terminal.Output;
        output.ShouldContain("Keyboard Controls");
        output.ShouldContain("Gameplay");
        output.ShouldContain("Playback");
        output.ShouldContain("Promotion");
        output.ShouldContain("Custom Setup");
    }

    [Fact]
    public void RenderBoard_InPlaybackMode_ShowsPlaybackInfo()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        // Make a move so we can enter playback via NavigateBack
        game.TryMove(DoMove(E2, E4));
        display.UI.NavigateBack();

        terminal.ClearOutput();
        display.RenderMove(game, UIResponse.NeedsRefresh, [], null);

        terminal.Output.ShouldContain("Playback");
    }

    [Fact]
    public void RenderBoard_Checkmate_ShowsCheckmateMessage()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        // Fool's mate: 1. f3 e5 2. g4 Qh4#
        game.TryMove(DoMove(F2, F3));
        game.TryMove(DoMove(E7, E5));
        game.TryMove(DoMove(G2, G4));
        game.TryMove(DoMove(D8, H4));

        display.RenderMove(game, UIResponse.NeedsRefresh, [], null);

        terminal.Output.ShouldContain("Checkmate");
    }

    [Fact]
    public void HandleResize_DoesNotThrow()
    {
        var (display, _) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);

        Should.NotThrow(() => display.HandleResize(game));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var (display, _) = CreateDisplay();

        Should.NotThrow(() => display.Dispose());
    }

    [Fact]
    public void RenderMove_SetupMode_ShowsSetupMessage()
    {
        var (display, terminal) = CreateDisplay();
        var game = new Game();
        display.ResetGame(game);
        display.UI.IsSetupMode = true;

        display.RenderMove(game, UIResponse.IsUpdate | UIResponse.NeedsPiecePlacement, [], null);

        terminal.Output.ShouldContain("Setup");
    }
}
