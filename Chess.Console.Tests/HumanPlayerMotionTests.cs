using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Position;

namespace Chess.Console.Tests;

/// <summary>
/// Phase 2 of docs/drag-ghost.md: the terminal feeds pointer motion to <see cref="GameUI"/> through
/// the ordinary player path, and it is the ONLY display family that uses the clip rects that come
/// back — <c>ConsoleGameDisplayBase.RenderFrame</c> unions them and renders partially, where
/// <c>PixelGameDisplay.RenderMove</c> drops them. So this is where phase 1's damage model is
/// validated rather than merely accepted.
/// </summary>
public class HumanPlayerMotionTests
{
    private static ConsoleInputEvent Press(int x, int y)
        => new(new MouseEvent(0, x, y, IsRelease: false), ConsoleKey.None, 0);

    private static ConsoleInputEvent Motion(int x, int y)
        => new(new MouseEvent(0, x, y, IsRelease: false) { IsMotion = true }, ConsoleKey.None, 0);

    private static ConsoleInputEvent Release(int x, int y)
        => new(new MouseEvent(0, x, y, IsRelease: true), ConsoleKey.None, 0);

    private static ConsoleInputEvent Key(ConsoleKey key) => new(null, key, 0);

    private static (int X, int Y) Centre(GameUI ui, Position square)
    {
        var rect = ui.SquareRect(square);
        return ((int)(rect.UpperLeft.X + rect.Width / 2), (int)(rect.UpperLeft.Y + rect.Height / 2));
    }

    private static (GameUI Ui, HumanPlayer Player, Queue<ConsoleInputEvent> Inputs) Setup()
    {
        var ui = new GameUI(new Game(), 800, 800) { IsSetupMode = true };
        var inputs = new Queue<ConsoleInputEvent>();
        return (ui, new HumanPlayer(new TestableTerminal(inputs)), inputs);
    }

    [Fact]
    public void Motion_WithAPieceInHand_MovesTheGhostAndReportsItsDamage()
    {
        var (ui, player, inputs) = Setup();
        var (px, py) = Centre(ui, D2);
        inputs.Enqueue(Press(px, py));
        player.TryMakeMove(ui);
        ui.PickedUp.ShouldBe(D2);

        var (mx, my) = Centre(ui, D5);
        inputs.Enqueue(Motion(mx, my));
        var result = player.TryMakeMove(ui);

        result.ShouldNotBeNull();
        result.Value.Response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        result.Value.ClipRects.ShouldNotBeEmpty();
        ui.GhostRect.ShouldNotBeNull();
    }

    /// <summary>Motion outside setup mode is inert — there is nothing in hand for it to move, and the
    /// gate lives in GameUI so every host gets it rather than each input mapping.</summary>
    [Fact]
    public void Motion_OutsideSetupMode_IsInert()
    {
        var ui = new GameUI(new Game(), 800, 800);
        var inputs = new Queue<ConsoleInputEvent>();
        var player = new HumanPlayer(new TestableTerminal(inputs));

        var (mx, my) = Centre(ui, D5);
        inputs.Enqueue(Motion(mx, my));
        var result = player.TryMakeMove(ui);

        result.ShouldNotBeNull();
        result.Value.Response.ShouldBe(UIResponse.None);
        result.Value.ClipRects.ShouldBeEmpty();
        ui.GhostRect.ShouldBeNull();
    }

    /// <summary>
    /// A motion event with another event already queued behind it renders nothing: the position it
    /// carries is stale before a partial sixel encode could put it on screen. The STATE still
    /// advances — only the paint is dropped.
    /// </summary>
    [Fact]
    public void Motion_WithMoreInputQueued_RendersNothingButStillMovesTheGhost()
    {
        var (ui, player, inputs) = Setup();
        var (px, py) = Centre(ui, D2);
        inputs.Enqueue(Press(px, py));
        player.TryMakeMove(ui);

        var (firstX, firstY) = Centre(ui, D4);
        var (secondX, secondY) = Centre(ui, D5);
        inputs.Enqueue(Motion(firstX, firstY));
        inputs.Enqueue(Motion(secondX, secondY));

        var coalesced = player.TryMakeMove(ui);

        coalesced.ShouldNotBeNull();
        coalesced.Value.Response.ShouldBe(UIResponse.None);
        coalesced.Value.ClipRects.ShouldBeEmpty();
        ui.DragPoint.ShouldBe(new PointInt(firstX, firstY));
    }

    /// <summary>
    /// The damage a coalesced event dirtied is NOT stale, and is carried to whichever event does
    /// render. Without it the pixels the ghost vacated stay on screen: the terminal renders partially,
    /// so what is not in the clip rect is simply never repainted.
    /// </summary>
    [Fact]
    public void Motion_Coalesced_CarriesItsDamageToTheEventThatDoesRender()
    {
        var (ui, player, inputs) = Setup();
        var (px, py) = Centre(ui, D2);
        inputs.Enqueue(Press(px, py));
        player.TryMakeMove(ui);

        var (firstX, firstY) = Centre(ui, D4);
        inputs.Enqueue(Motion(firstX, firstY));
        inputs.Enqueue(Motion(px + 2, py + 2));
        player.TryMakeMove(ui);                  // coalesced: renders nothing
        var vacated = ui.GhostRect!.Value;       // ... but this footprint is now dirty

        var last = player.TryMakeMove(ui);       // the queue is empty, so this one paints

        last.ShouldNotBeNull();
        last.Value.Response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();

        var clip = last.Value.ClipRects[0];
        for (var i = 1; i < last.Value.ClipRects.Length; i++) clip = clip.Union(last.Value.ClipRects[i]);

        // RenderFrame unions the rects into one clip, so the union is what actually gets repainted.
        clip.Contains(vacated.UpperLeft.X, vacated.UpperLeft.Y).ShouldBeTrue();
        clip.Contains(vacated.LowerRight.X - 1, vacated.LowerRight.Y - 1).ShouldBeTrue();
    }

    /// <summary>
    /// The event that flushes deferred damage may itself ask for nothing — an unmapped key returns
    /// None with no rects, and would swallow the drag's last frame if the flush did not force a
    /// repaint of its own.
    /// </summary>
    [Fact]
    public void Motion_CoalescedThenAnInertEvent_StillPaintsTheDeferredDamage()
    {
        var (ui, player, inputs) = Setup();
        var (px, py) = Centre(ui, D2);
        inputs.Enqueue(Press(px, py));
        player.TryMakeMove(ui);

        var (mx, my) = Centre(ui, D4);
        inputs.Enqueue(Motion(mx, my));
        inputs.Enqueue(Key(ConsoleKey.NoName));   // maps to nothing
        player.TryMakeMove(ui);                   // coalesced
        var vacated = ui.GhostRect!.Value;

        var flushed = player.TryMakeMove(ui);

        flushed.ShouldNotBeNull();
        flushed.Value.Response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        flushed.Value.ClipRects.ShouldNotBeEmpty();
        flushed.Value.ClipRects[0].Contains(vacated.UpperLeft.X, vacated.UpperLeft.Y).ShouldBeTrue();
    }

    /// <summary>
    /// A full-frame repaint (an EMPTY clip list, which is how this codebase spells "everything")
    /// already covers the deferred region. Merging into it would narrow a full frame to the ghost's
    /// rects, which is the opposite of what the deferral is for.
    /// </summary>
    [Fact]
    public void Motion_CoalescedThenAFullRepaint_DoesNotNarrowIt()
    {
        var (ui, player, inputs) = Setup();
        var (px, py) = Centre(ui, D2);
        inputs.Enqueue(Press(px, py));
        player.TryMakeMove(ui);

        var (mx, my) = Centre(ui, D4);
        inputs.Enqueue(Motion(mx, my));
        inputs.Enqueue(Release(mx, my));          // the drop: NeedsRefresh with no rects
        player.TryMakeMove(ui);                   // coalesced

        var drop = player.TryMakeMove(ui);

        drop.ShouldNotBeNull();
        drop.Value.Response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        drop.Value.ClipRects.ShouldBeEmpty();
        ui.PickedUp.ShouldBeNull();
        ui.GhostRect.ShouldBeNull();
    }

    /// <summary>The whole gesture end to end: press, drag across the board, release. The ghost follows
    /// and the piece lands where it was dropped.</summary>
    [Fact]
    public void PressDragRelease_MovesThePieceAndLeavesNoGhost()
    {
        var game = new Game();
        var ui = new GameUI(game, 800, 800) { IsSetupMode = true };
        var inputs = new Queue<ConsoleInputEvent>();
        var player = new HumanPlayer(new TestableTerminal(inputs));

        var (px, py) = Centre(ui, D2);
        var (mx, my) = Centre(ui, D4);
        var (rx, ry) = Centre(ui, D5);

        inputs.Enqueue(Press(px, py));
        player.TryMakeMove(ui);
        inputs.Enqueue(Motion(mx, my));
        player.TryMakeMove(ui);
        ui.GhostRect.ShouldNotBeNull();

        inputs.Enqueue(Release(rx, ry));
        player.TryMakeMove(ui);

        game.Board[D2].PieceType.ShouldBe(PieceType.None);
        game.Board[D5].ShouldBe(new Piece(PieceType.Pawn, Side.White));
        ui.GhostRect.ShouldBeNull();
    }
}
