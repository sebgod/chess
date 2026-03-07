using Console.Lib;
using DIR.Lib;
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

        var statusBar = layout.Dock(DockStyle.Bottom, 1);
        var history = layout.Dock(DockStyle.Right, 24);
        var board = layout.Dock(DockStyle.Fill);

        statusBar.Size.ShouldBe((80, 1));
        history.Size.ShouldBe((24, 23));
        board.Size.ShouldBe((56, 23));
    }

    [Fact]
    public void Layout_DockBottomThenRight_ProducesCorrectPositions()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var layout = new TerminalLayout(terminal);

        var statusBar = layout.Dock(DockStyle.Bottom, 1);
        var history = layout.Dock(DockStyle.Right, 24);
        var board = layout.Dock(DockStyle.Fill);

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

        var statusBar = layout.Dock(DockStyle.Bottom, 1);
        var history = layout.Dock(DockStyle.Right, 24);
        var board = layout.Dock(DockStyle.Fill);

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

        var statusBar = layout.Dock(DockStyle.Bottom, 1);
        var history = layout.Dock(DockStyle.Right, 24);
        var board = layout.Dock(DockStyle.Fill);

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
        var board = layout.Dock(DockStyle.Fill);
        var statusBar = layout.Dock(DockStyle.Bottom, 1);
        var history = layout.Dock(DockStyle.Right, 24);

        statusBar.Size.ShouldBe((80, 1));
        history.Size.ShouldBe((24, 23));
        board.Size.ShouldBe((56, 23));
    }

    [Fact]
    public void Canvas_Render_PerformsFullBlit()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 0, 0, 56, 23);
        var renderer = new FakeSixelRenderer(460);
        var canvas = new Canvas<object>(viewport, renderer);

        canvas.Render();

        renderer.FullEncodes.ShouldBe(1);
        renderer.PartialEncodes.ShouldBe(0);
        terminal.LastCursorPosition.ShouldBe((0, 0));
    }

    [Fact]
    public void Canvas_Render_PartialClip_AlignsToCell()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        // CellSize is (10, 20) from FakeTerminal
        var viewport = new TerminalViewport(terminal, 0, 0, 56, 23);
        var renderer = new FakeSixelRenderer(460);
        var canvas = new Canvas<object>(viewport, renderer);

        // Clip from pixel (0,25) to (560,95) — Y bounds align to cell rows: startRow=1 (y=20), endRow=5 (y=100)
        canvas.Render(new RectInt(new PointInt(560, 95), new PointInt(0, 25)));

        renderer.FullEncodes.ShouldBe(0);
        renderer.PartialEncodes.ShouldBe(1);
        renderer.LastStartY.ShouldBe(20);    // row 1 * cellHeight 20
        renderer.LastHeight.ShouldBe(80u);   // rows 1-4 * 20 = 80
        // Cursor should be at column 0, row 1 (startRow)
        terminal.LastCursorPosition.ShouldBe((0, 1));
    }

    [Fact]
    public void Canvas_Render_PartialClip_ClampsToRenderHeight()
    {
        var terminal = new FakeTerminal(new Queue<ConsoleInputEvent>(), 80, 24);
        var viewport = new TerminalViewport(terminal, 0, 0, 56, 23);
        var renderer = new FakeSixelRenderer(450);
        var canvas = new Canvas<object>(viewport, renderer);

        // Clip near bottom: Y 440 to 460, renderHeight=450
        // startRow = 440/20 = 22, endRow = (460+19)/20 = 23
        // pixelStartY = 440, pixelEndY = min(450, 460) = 450, cropHeight = 10
        canvas.Render(new RectInt(new PointInt(560, 460), new PointInt(0, 440)));

        renderer.LastHeight.ShouldBe(10u);
    }

    private sealed class FakeSixelRenderer(uint height) : SixelRenderer<object>(new())
    {
        public int FullEncodes { get; private set; }
        public int PartialEncodes { get; private set; }
        public int LastStartY { get; private set; }
        public uint LastHeight { get; private set; }

        public override uint Width => 560;
        public override uint Height => height;
        public override void Resize(uint width, uint height) { }
        public override void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth) { }
        public override void FillRectangle(in RectInt rect, RGBAColor32 fillColor) { }
        public override void FillEllipse(in RectInt rect, RGBAColor32 fillColor) { }
        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near) { }
        public override void Dispose() { }

        public override void EncodeSixel(Stream output) => FullEncodes++;
        public override void EncodeSixel(int startY, uint height1, Stream output)
        {
            PartialEncodes++;
            LastStartY = startY;
            LastHeight = height1;
        }
    }
}
