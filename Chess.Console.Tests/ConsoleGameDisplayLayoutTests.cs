using System.Text;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Console.Tests;

/// <summary>
/// The console frame's geometry: that the <see cref="DIR.Lib.Layout"/> tree places the history panel where
/// clicks are resolved against it, and that the panel's scroll capacity reaches
/// <see cref="GameUI.HistoryViewportRows"/>.
///
/// <para>Nothing here renders the board. <c>RenderMove</c> with <see cref="UIResponse.IsUpdate"/> alone
/// fills the widgets without setting <see cref="UIResponse.NeedsRefresh"/>, so the Sixel path never runs —
/// the history list still gets its items, which is all these assertions need.</para>
/// </summary>
public class ConsoleGameDisplayLayoutTests
{
    private const int HistoryColumns = 24;
    private const int CellWidth = 10;
    private const int CellHeight = 20;

    private sealed class FakeTerminal(int width, int height) : IVirtualTerminal
    {
        private readonly StringBuilder _output = new();

        public (int Width, int Height) Size { get; set; } = (width, height);

        /// <summary>How often the app blanked the screen — the sixel-artifact remedy.</summary>
        public int ClearCount { get; private set; }

        // ITerminalViewport
        public (int Column, int Row) Offset => (0, 0);
        public TermCell CellSize => new(CellWidth, CellHeight);
        public ColorMode ColorMode => ColorMode.Sgr16;
        public void SetCursorPosition(int left, int top) { }
        public void Write(string text) => _output.Append(text);
        public void WriteLine(string? text = null) { _output.Append(text); _output.Append('\n'); }
        public void Flush() { }
        public Stream OutputStream => Stream.Null;

        // IVirtualTerminal
        public Task InitAsync() => Task.CompletedTask;
        public ImageDisplayCapability ImageDisplayCapability => ImageDisplayCapability.NoColor;
        public bool HasSixelSupport => false;
        public bool HasColorSupport => false;
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => false;
        public void EnterAlternateScreen() { }
        public bool IsAlternateScreen => false;
        public void Clear() { ClearCount++; _output.Clear(); }
        public bool HasInput() => false;
        public ConsoleInputEvent TryReadInput() => default;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Twelve moves, chosen only for being uneventful: every pawn on its own file, then knights, rooks and
    /// bishops to squares their own pawns vacated. No capture, check or repetition that would end the game
    /// early, and enough rows to overflow the panel so a scrollbar appears.
    /// </summary>
    private static Game LongEnoughToScroll()
    {
        var game = new Game();
        (Position From, Position To)[] moves =
        [
            (Position.A2, Position.A4), (Position.A7, Position.A5),
            (Position.B2, Position.B4), (Position.B7, Position.B5),
            (Position.C2, Position.C4), (Position.C7, Position.C5),
            (Position.D2, Position.D4), (Position.D7, Position.D5),
            (Position.E2, Position.E4), (Position.E7, Position.E5),
            (Position.F2, Position.F4), (Position.F7, Position.F5),
            (Position.G2, Position.G4), (Position.G7, Position.G5),
            (Position.H2, Position.H4), (Position.H7, Position.H5),
            (Position.B1, Position.C3), (Position.B8, Position.C6),
            (Position.G1, Position.F3), (Position.G8, Position.F6),
            (Position.A1, Position.A3), (Position.A8, Position.A6),
            (Position.C1, Position.E3), (Position.C8, Position.E6),
        ];

        foreach (var (from, to) in moves)
        {
            game.TryMove(from, to).IsMoveOrCapture().ShouldBeTrue($"{from}{to} should apply");
        }

        game.IsFinished.ShouldBeFalse();
        return game;
    }

    // A 60x12 terminal gives the history panel columns 36..59 and rows 0..10 (the status bar takes row 11),
    // so the list shows 10 data rows under its header -- fewer than the 12 moves above, which is what puts
    // a scrollbar in the last column.
    //
    // The board has to stay big enough to be real: GameUI aligns its square size to whole terminal cells,
    // so a board under 160px on its short side rounds the squares down to ZERO and every hit test divides
    // by it. 60x12 leaves the board 36x11 cells = 360x220px, which rounds to 20px squares.
    private static (SixelGameDisplay Display, FakeTerminal Terminal, Game Game) Setup(int width = 60, int height = 12)
    {
        var terminal = new FakeTerminal(width, height);
        var display = new SixelGameDisplay(terminal);
        var game = LongEnoughToScroll();

        display.ResetGame(game);
        display.RenderMove(game, UIResponse.IsUpdate, []);

        return (display, terminal, game);
    }

    private static int ColumnToPixel(int column) => column * CellWidth + CellWidth / 2;
    private static int RowToPixel(int row) => row * CellHeight + CellHeight / 2;

    [Fact]
    public void ResetGame_GivesTheUiThePanelsScrollCapacity()
    {
        // 12 terminal rows - 1 status bar - 1 list header = 10 data rows. Left at 0 (the old behaviour,
        // where only HandleResize set it) every history scroll path divides by it and misbehaves.
        var (display, _, _) = Setup();

        display.UI.HistoryViewportRows.ShouldBe(10);
    }

    [Fact]
    public void HistoryClick_OnAContentRow_NavigatesToThatMove()
    {
        var (display, _, _) = Setup();

        // Column 36 is the panel's first content column; row 1 is its first row under the header.
        var (response, _) = display.UI.HandleMouseDown(ColumnToPixel(36), RowToPixel(1));

        response.ShouldNotBe(UIResponse.None, "the click must reach the history panel at all");
        display.UI.Mode.ShouldBe(GameUIMode.Playback);
    }

    /// <summary>
    /// The defect: Widget.HitTest reports the scrollbar's column like any other, so splitting the row at
    /// half the FULL viewport width made a click on the track resolve to Black's ply and jump into
    /// playback. Asking the list (HitTestRow) excludes that column instead.
    /// </summary>
    [Fact]
    public void HistoryClick_OnTheScrollbarColumn_IsInert()
    {
        var (display, _, _) = Setup();

        // Column 59 is the panel's last column, which the scrollbar owns once the list overflows.
        var (response, _) = display.UI.HandleMouseDown(ColumnToPixel(59), RowToPixel(1));

        response.ShouldBe(UIResponse.None);
        display.UI.Mode.ShouldNotBe(GameUIMode.Playback);
    }

    [Fact]
    public void HistoryClick_RightHalfOfARow_PicksBlacksPly()
    {
        var (display, _, _) = Setup();

        // Content spans columns 36..58 (59 is the scrollbar), so its right half starts at column 47.
        display.UI.HandleMouseDown(ColumnToPixel(50), RowToPixel(1));

        display.UI.Mode.ShouldBe(GameUIMode.Playback);
        (display.UI.PlaybackPlyIndex % 2).ShouldBe(1, "the right half of a row is Black's ply");
    }

    [Fact]
    public void Resize_ReArrangesAndUpdatesTheScrollCapacity()
    {
        var (display, terminal, game) = Setup();
        display.UI.HistoryViewportRows.ShouldBe(10);

        terminal.Size = (80, 20);
        display.HandleResize(game);

        // 20 rows - 1 status - 1 header = 18.
        display.UI.HistoryViewportRows.ShouldBe(18);
    }

    /// <summary>
    /// Shrinking the terminal used to leave the previous, larger frame on screen around the new one:
    /// sixel pixels are not erased by drawing a smaller image over them, and repainting cells does not
    /// touch them. Observed in a real terminal as a stale board, stale rank labels and stale captured
    /// strips surrounding the live one.
    /// </summary>
    [Fact]
    public void Resize_BlanksTheScreenBeforeRepainting()
    {
        var (display, terminal, game) = Setup();
        var before = terminal.ClearCount;

        terminal.Size = (50, 10);
        display.HandleResize(game);

        terminal.ClearCount.ShouldBe(before + 1);
    }

    [Fact]
    public void Resize_WithNothingChanged_DoesNotBlankTheScreen()
    {
        // HandleResize runs every pump, so clearing unconditionally would blank the terminal continuously.
        var (display, terminal, game) = Setup();
        var before = terminal.ClearCount;

        display.HandleResize(game);

        terminal.ClearCount.ShouldBe(before);
    }

    [Fact]
    public void Resize_WithNothingChanged_IsANoOp()
    {
        // GameLoop calls HandleResize every pump, so the "did anything move" guard is what stops the
        // display repainting continuously.
        var (display, _, game) = Setup();
        var before = display.UI;

        display.HandleResize(game);

        display.UI.ShouldBeSameAs(before, "an unchanged arrangement must not rebuild GameUI");
    }
}
