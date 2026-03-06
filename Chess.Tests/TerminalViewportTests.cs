using Console.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public sealed class TerminalViewportTests
{
    [Fact]
    public void Size_ReturnsViewportDimensions()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 10, 5, 30, 15);

        viewport.Size.ShouldBe((30, 15));
    }

    [Fact]
    public void SetCursorPosition_TranslatesLocalToParentCoordinates()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 56, 0, 24, 23);

        viewport.SetCursorPosition(0, 0);

        terminal.LastCursorPosition.ShouldBe((56, 0));
    }

    [Fact]
    public void SetCursorPosition_AddsOffsetToCoordinates()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 10, 5, 30, 15);

        viewport.SetCursorPosition(3, 7);

        terminal.LastCursorPosition.ShouldBe((13, 12));
    }

    [Fact]
    public void SetCursorPosition_ClampsNegativeToZero()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 10, 5, 30, 15);

        viewport.SetCursorPosition(-5, -3);

        terminal.LastCursorPosition.ShouldBe((10, 5));
    }

    [Fact]
    public void SetCursorPosition_ClampsToSizeMinusOne()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 10, 5, 30, 15);

        viewport.SetCursorPosition(50, 20);

        terminal.LastCursorPosition.ShouldBe((39, 19));
    }

    [Fact]
    public void NestedViewports_ComposeOffsetsCorrectly()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 120, 40);
        var outer = new TerminalViewport(terminal, 10, 5, 60, 30);
        var inner = new TerminalViewport(outer, 3, 2, 20, 10);

        inner.SetCursorPosition(1, 1);

        // inner (1,1) -> outer (3+1, 2+1) = outer (4, 3)
        // outer (4, 3) -> terminal (10+4, 5+3) = terminal (14, 8)
        terminal.LastCursorPosition.ShouldBe((14, 8));
    }

    [Fact]
    public void Write_DelegatesToParent()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 0, 0, 40, 12);

        // Should not throw - delegates to parent's Write
        viewport.Write("test");
        viewport.WriteLine("line");
    }

    [Fact]
    public void Colors_AppliedToParentOnWrite()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 0, 0, 40, 12);

        viewport.ForegroundColor = ConsoleColor.Red;
        viewport.BackgroundColor = ConsoleColor.Blue;

        // Colors are local until a write pushes them to the parent
        terminal.ForegroundColor.ShouldNotBe(ConsoleColor.Red);

        viewport.Write("test");
        terminal.ForegroundColor.ShouldBe(ConsoleColor.Red);
        terminal.BackgroundColor.ShouldBe(ConsoleColor.Blue);

        viewport.ResetColor();
    }

    [Fact]
    public void Colors_IsolatedBetweenViewports()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var vp1 = new TerminalViewport(terminal, 0, 0, 40, 12);
        var vp2 = new TerminalViewport(terminal, 40, 0, 40, 12);

        vp1.ForegroundColor = ConsoleColor.Red;
        vp2.ForegroundColor = ConsoleColor.Green;

        vp1.Write("a");
        terminal.ForegroundColor.ShouldBe(ConsoleColor.Red);

        vp2.Write("b");
        terminal.ForegroundColor.ShouldBe(ConsoleColor.Green);

        // vp1's local state is unchanged
        vp1.ForegroundColor.ShouldBe(ConsoleColor.Red);
    }

    [Fact]
    public void UpdateGeometry_ChangesSizeAndOffset()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 0, 0, 10, 10);

        viewport.UpdateGeometry(5, 3, 20, 15);

        viewport.Size.ShouldBe((20, 15));
        viewport.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((5, 3));
    }

    [Fact]
    public void Layout_DockBottomThenRight_ProducesCorrectSizes()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var layout = new TerminalLayout(terminal);

        var statusBar = layout.Dock(Dock.Bottom, 1);
        var history = layout.Dock(Dock.Right, 24);
        var board = layout.Dock(Dock.Fill);

        statusBar.Size.ShouldBe((80, 1));
        history.Size.ShouldBe((24, 23));
        board.Size.ShouldBe((56, 23));
    }

    [Fact]
    public void Layout_DockBottomThenRight_ProducesCorrectPositions()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var layout = new TerminalLayout(terminal);

        var statusBar = layout.Dock(Dock.Bottom, 1);
        var history = layout.Dock(Dock.Right, 24);
        var board = layout.Dock(Dock.Fill);

        // Status bar at row 23 (bottom), full width
        statusBar.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((0, 23));

        // History at col 56 (right side), row 0
        history.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((56, 0));

        // Board at origin
        board.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((0, 0));
    }

    [Fact]
    public void Layout_Recompute_ReturnsTrueOnResize()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var layout = new TerminalLayout(terminal);

        var statusBar = layout.Dock(Dock.Bottom, 1);
        var history = layout.Dock(Dock.Right, 24);
        var board = layout.Dock(Dock.Fill);

        // No change
        layout.Recompute().ShouldBeFalse();

        // Resize terminal
        terminal.Resize(100, 30);
        layout.Recompute().ShouldBeTrue();

        // Viewports should be updated
        statusBar.Size.ShouldBe((100, 1));
        history.Size.ShouldBe((24, 29));
        board.Size.ShouldBe((76, 29));

        // Positions updated too
        statusBar.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((0, 29));

        history.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((76, 0));

        board.SetCursorPosition(0, 0);
        terminal.LastCursorPosition.ShouldBe((0, 0));
    }

    [Fact]
    public void Layout_SmallTerminal_ClampsPanels()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 10, 3);
        var layout = new TerminalLayout(terminal);

        var statusBar = layout.Dock(Dock.Bottom, 1);
        var history = layout.Dock(Dock.Right, 24);
        var board = layout.Dock(Dock.Fill);

        statusBar.Size.ShouldBe((10, 1));
        // Only 2 rows remain after status bar; history wants 24 cols but only 10 available
        history.Size.ShouldBe((10, 2));
        // Nothing left for the board
        board.Size.ShouldBe((0, 2));
    }

    [Fact]
    public void Layout_FillDockedBeforeEdges_StillGetsRemainder()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var layout = new TerminalLayout(terminal);

        // Fill registered first, edges after
        var board = layout.Dock(Dock.Fill);
        var statusBar = layout.Dock(Dock.Bottom, 1);
        var history = layout.Dock(Dock.Right, 24);

        statusBar.Size.ShouldBe((80, 1));
        history.Size.ShouldBe((24, 23));
        board.Size.ShouldBe((56, 23));
    }
}
