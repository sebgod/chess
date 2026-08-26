using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.Position;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.Tests;

public class GameUITests
{
    private static GameUI CreateUI(Game game) => new(game, 800, 800);

    private static GameUI CreateStandardUI() => CreateUI(new Game());

    // ── Safe-area top offset ───────────────────────────────────────

    [Fact]
    public void TopOffset_ShiftsPixelHitTestingWithTheBoard()
    {
        // top/leftOffset shift drawing AND FindSelected's pixel->square mapping together — a tap
        // that hit e2 unshifted hits e2 only when adjusted by both offsets (landscape phones put
        // the cutout on the SIDE, so x matters as much as y).
        const int topOffset = 137; // > one square at 800x800, so an unadjusted tap can't still hit e2
        const int leftOffset = 61; // < one square: shifts the file by exactly one column at the edge
        var game = new Game();
        var ui = CreateUI(game);
        var shifted = new GameUI(game, 800, 800, topOffset: topOffset, leftOffset: leftOffset);

        // Locate e2's first (top-left-most) pixel on the unshifted board.
        var (fx, fy) = (-1, -1);
        for (var y = 0; y < 800 && fx < 0; y++)
            for (var x = 0; x < 800; x++)
                if (ui.FindSelected(x, y) == E2) { (fx, fy) = (x, y); break; }
        fx.ShouldBeGreaterThanOrEqualTo(0, "probe never found e2 on the unshifted board");

        shifted.FindSelected(fx, fy).ShouldNotBe(E2);
        shifted.FindSelected(fx + leftOffset, fy).ShouldNotBe(E2);
        shifted.FindSelected(fx, fy + topOffset).ShouldNotBe(E2); // x unadjusted -> lands a file left of e
        shifted.FindSelected(fx + leftOffset, fy + topOffset).ShouldBe(E2);
    }

    // ── Selection ──────────────────────────────────────────────────

    [Fact]
    public void Select_ValidPiece_SetsSelected()
    {
        var ui = CreateStandardUI();

        var (response, clips) = ui.TryPerformAction(E2);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Selected.ShouldBe(E2);
        // The dots are deterministic — the selected square plus every legal destination — so the
        // clips name them instead of asking for the whole board. e2's pawn can go to e3 and e4.
        clips.ShouldBe([ui.SquareRect(E2), ui.SquareRect(E3), ui.SquareRect(E4)], ignoreOrder: true);
    }

    [Fact]
    public void Select_EmptySquare_WithUniqueMove_PerformsMove()
    {
        // Clicking E4 on a standard board finds the E2-E4 pawn push automatically
        var ui = CreateStandardUI();

        var (response, _) = ui.TryPerformAction(E4);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Game.Board[E4].PieceType.ShouldBe(PieceType.Pawn);
    }

    [Fact]
    public void Select_EmptySquare_NoValidMove_DoesNothing()
    {
        // E5 has no unique valid move to it on the first turn
        var ui = CreateStandardUI();

        var (response, _) = ui.TryPerformAction(E5);

        response.ShouldBe(UIResponse.None);
        ui.Selected.ShouldBeNull();
    }

    [Fact]
    public void Select_OpponentPiece_DoesNotSelect()
    {
        var ui = CreateStandardUI();

        var (response, _) = ui.TryPerformAction(E7);

        response.ShouldBe(UIResponse.None);
        ui.Selected.ShouldBeNull();
    }

    [Fact]
    public void ClearSelection_AfterSelect_ClearsAndReturnsClipRect()
    {
        var ui = CreateStandardUI();
        ui.TryPerformAction(E2);

        var (response, clips) = ui.ClearSelection();

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Selected.ShouldBeNull();
        // The erase set equals the draw set: the game hasn't changed, so the dots to remove are
        // recomputed from the selection being cleared.
        clips.ShouldBe([ui.SquareRect(E2), ui.SquareRect(E3), ui.SquareRect(E4)], ignoreOrder: true);
    }

    [Fact]
    public void Reselect_ClipRects_CoverBothTheOldAndTheNewDots()
    {
        // Public flows can't reselect (a click on another own piece attempts a move), but TrySelect
        // is public, so a direct reselection must erase the old dot set as well as draw the new one.
        var ui = CreateStandardUI();
        ui.TrySelect(E2);

        var (response, clips) = ui.TrySelect(D2);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Selected.ShouldBe(D2);
        clips.ShouldBe(
            [
                ui.SquareRect(E2), ui.SquareRect(E3), ui.SquareRect(E4),
                ui.SquareRect(D2), ui.SquareRect(D3), ui.SquareRect(D4),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Move_ClipRects_IncludeTheDotsTheMoveErases()
    {
        // g1's knight shows dots on f3 AND h3; moving g1-f3 must invalidate h3 too, or its dot
        // lingers wherever the move renders partially (the stacked layout — the flanked one happens
        // to repaint fully because the captured tray goes stale on every ply). h3 is the
        // discriminating square: nothing else about this move touches the h-file.
        var ui = CreateStandardUI();
        ui.TryPerformAction(G1);

        var (response, clips) = ui.TryPerformAction(F3);

        response.HasFlag(UIResponse.IsUpdate).ShouldBeTrue();
        clips.ShouldContain(ui.SquareRect(H3));
    }

    [Fact]
    public void Move_WithoutSelection_HasNoDotsToErase()
    {
        // An engine move arrives as a bare Action with nothing selected — no dots were painted, so
        // none are invalidated. The knight's OTHER destination is the discriminator: g1-f3's own
        // rects (from, to, their span) never reach the h-file.
        var ui = CreateStandardUI();

        var (_, clips) = ui.TryPerformAction(DoMove(G1, F3));

        clips.ShouldNotContain(ui.SquareRect(H3), "no selection ever painted a dot on h3");
    }

    [Fact]
    public void ClearSelection_WhenNothingSelected_ReturnsNone()
    {
        var ui = CreateStandardUI();

        var (response, clips) = ui.ClearSelection();

        response.ShouldBe(UIResponse.None);
        clips.ShouldBeEmpty();
    }

    // ── Move execution and clip rects ──────────────────────────────

    [Fact]
    public void Move_SimpleMove_ReturnsFromAndToClipRects()
    {
        var ui = CreateStandardUI();
        ui.TryPerformAction(E2);

        var (response, clips) = ui.TryPerformAction(E4);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        response.HasFlag(UIResponse.IsUpdate).ShouldBeTrue();
        clips.ShouldContain(ui.SquareRect(E2));
        clips.ShouldContain(ui.SquareRect(E4));
    }

    [Fact]
    public void Move_ClipRects_CoverThePreviousMoveArrow()
    {
        // The last-move arrow is drawn from the origin square's centre to the destination's, so it
        // covers ground that neither end square owns. When the next move retires it, every pixel it
        // touched has to be invalidated or it survives as a ghost: the console displays union the
        // clip rects into one bounding box and repaint only that, leaving everything outside as it
        // was. Invalidating just the previous destination is not enough.
        var ui = CreateStandardUI();

        // 1. Nf3 — arrow g1->f3, deliberately a knight so it is neither rank- nor file-aligned.
        ui.TryPerformAction(G1);
        ui.TryPerformAction(F3);

        // 1... e5 — far enough away that the new move's own rects cannot cover g1 by accident.
        ui.TryPerformAction(E7);
        var (_, clips) = ui.TryPerformAction(E5);

        var union = clips[0];
        for (var i = 1; i < clips.Length; i++)
        {
            union = union.Union(clips[i]);
        }

        ui.SquareRect(G1).IsContainedWithin(union).ShouldBeTrue(
            "the retired arrow started on g1, so g1 must be repainted or the arrow tail lingers");
        ui.SquareRect(F3).IsContainedWithin(union).ShouldBeTrue(
            "the retired arrow ended on f3");
    }

    [Fact]
    public void Move_Capture_IncludesCapturedTextRects()
    {
        // Set up position where white can capture
        var board = new Board
        {
            [E1] = (Side.White, PieceType.King),
            [D4] = (Side.White, PieceType.Bishop),
            [G7] = (Side.Black, PieceType.Pawn),
            [E8] = (Side.Black, PieceType.King),
        };
        var game = new Game(board, Side.White, []);
        var ui = CreateUI(game);

        ui.TryPerformAction(D4);
        var (response, clips) = ui.TryPerformAction(G7);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        // Should have more clip rects than just from/to (captured text areas)
        clips.Length.ShouldBeGreaterThan(2);
    }

    [Fact]
    public void EnPassant_ClipRects_IncludeTakenPawnSquare()
    {
        // White pawn on e5, black just played d7-d5: en passant e5xd6
        var board = Board.StandardBoard + DoMove(E2, E5) + DoMove(D7, D5);
        var plies = ImmutableList.Create(
            new RecordedPly(E2, E5, ActionResult.Move, PieceType.Pawn),
            new RecordedPly(D7, D5, ActionResult.Move, PieceType.Pawn)
        );
        var game = new Game(board, Side.White, plies);
        var ui = CreateUI(game);

        ui.TryPerformAction(E5);
        var (response, clips) = ui.TryPerformAction(D6);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        // The taken pawn is on D5, not D6 — clip rects must include D5
        clips.ShouldContain(ui.SquareRect(D5));
    }

    [Fact]
    public void EnPassant_CapturedPawn_ReachesTheCapturedTally()
    {
        // The e.p. victim is the one capture whose piece is not on the destination square. The tray
        // tally used to pattern-match Capture/CaptureAndPromotion and skipped EnPassant entirely, so
        // the pawn vanished from the game without ever appearing in a pile (found live: 7. b5a6 e.p.
        // left White's pile empty while Black's own captures all showed).
        var board = Board.StandardBoard + DoMove(E2, E5) + DoMove(D7, D5);
        var plies = ImmutableList.Create(
            new RecordedPly(E2, E5, ActionResult.Move, PieceType.Pawn),
            new RecordedPly(D7, D5, ActionResult.Move, PieceType.Pawn)
        );
        var game = new Game(board, Side.White, plies);
        var ui = CreateUI(game);

        ui.TryPerformAction(E5);
        ui.TryPerformAction(D6); // exd6 e.p.

        Span<byte> counts = stackalloc byte[2 * 7]; // 2 sides × GameUI.PieceTypeStride
        ui.CountCaptured(counts);

        // White moved on the even ply, so its pile is the first block; the victim is a pawn.
        counts[(int)PieceType.Pawn].ShouldBe((byte)1, "the e.p. victim belongs in White's pile");
        var total = 0;
        foreach (var count in counts) total += count;
        total.ShouldBe(1, "the e.p. pawn is the game's only capture");
    }

    [Fact]
    public void Castling_Kingside_ClipRectsIncludeKingDestination()
    {
        var board = new Board
        {
            [E1] = (Side.White, PieceType.King),
            [H1] = (Side.White, PieceType.Rook),
            [E8] = (Side.Black, PieceType.King),
        };
        var game = new Game(board, Side.White, []);
        var ui = CreateUI(game);

        ui.TryPerformAction(E1);
        var (response, clips) = ui.TryPerformAction(G1);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.LastMove.ShouldNotBeNull();
        ui.LastMove.Value.To.ShouldBe(G1);
        clips.ShouldContain(ui.SquareRect(G1));
    }

    [Fact]
    public void Castling_Queenside_ClipRectsIncludeRookSquares()
    {
        var board = new Board
        {
            [E1] = (Side.White, PieceType.King),
            [A1] = (Side.White, PieceType.Rook),
            [E8] = (Side.Black, PieceType.King),
        };
        var game = new Game(board, Side.White, []);
        var ui = CreateUI(game);

        ui.TryPerformAction(E1);
        var (response, clips) = ui.TryPerformAction(C1);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        // Should include rook source (A1) and rook destination (D1)
        clips.ShouldContain(ui.SquareRect(A1));
        clips.ShouldContain(ui.SquareRect(D1));
    }

    [Fact]
    public void Move_IntoCheck_ClipRectsIncludeKingSquare()
    {
        // White plays a move that puts black in check
        var board = new Board
        {
            [E1] = (Side.White, PieceType.King),
            [D1] = (Side.White, PieceType.Rook),
            [E8] = (Side.Black, PieceType.King),
            [A8] = (Side.Black, PieceType.Rook),
        };
        var game = new Game(board, Side.White, []);
        var ui = CreateUI(game);

        ui.TryPerformAction(D1);
        var (response, clips) = ui.TryPerformAction(D8);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        game.GameStatus.ShouldBe(GameStatus.Check);
        // Clip rects should include the checked king's square
        clips.ShouldContain(ui.SquareRect(E8));
    }

    // ── Promotion ──────────────────────────────────────────────────

    [Fact]
    public void Move_ToPromotionRank_SetsPendingPromotion()
    {
        var board = new Board
        {
            [A7] = (Side.White, PieceType.Pawn),
            [D3] = (Side.White, PieceType.King),
            [H7] = (Side.Black, PieceType.King),
        };
        var game = new Game(board, Side.White, []);
        var ui = CreateUI(game);

        ui.TryPerformAction(A7);
        var (response, _) = ui.TryPerformAction(A8);

        response.HasFlag(UIResponse.NeedsPromotionType).ShouldBeTrue();
        ui.PendingPromotion.ShouldBe(A8);
    }

    [Fact]
    public void Promote_WithPieceType_CompletesMove()
    {
        var board = new Board
        {
            [A7] = (Side.White, PieceType.Pawn),
            [D3] = (Side.White, PieceType.King),
            [H7] = (Side.Black, PieceType.King),
        };
        var game = new Game(board, Side.White, []);
        var ui = CreateUI(game);

        ui.TryPerformAction(A7);
        ui.TryPerformAction(A8);

        var (response, _) = ui.TryPerformAction(Promote(A7, A8, PieceType.Queen));

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.PendingPromotion.ShouldBeNull();
        game.Board[A8].PieceType.ShouldBe(PieceType.Queen);
    }

    // ── Playback navigation ────────────────────────────────────────

    [Fact]
    public void NavigateBack_EntersPlaybackMode()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        var ui = CreateUI(game);

        var (response, _) = ui.NavigateBack();

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Mode.ShouldBe(GameUIMode.Playback);
        ui.PlaybackPlyIndex.ShouldBe(0); // viewing after white's first move
    }

    [Fact]
    public void NavigateBack_NoMoves_ReturnsNone()
    {
        var ui = CreateStandardUI();

        var (response, _) = ui.NavigateBack();

        response.ShouldBe(UIResponse.None);
        ui.Mode.ShouldBe(GameUIMode.Playing);
    }

    [Fact]
    public void NavigateForward_PastEnd_ExitsPlayback()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        var ui = CreateUI(game);

        // NavigateBack with 2 plies: PlaybackPlyIndex = 2-1-1 = 0
        ui.NavigateBack();
        ui.Mode.ShouldBe(GameUIMode.Playback);
        ui.PlaybackPlyIndex.ShouldBe(0);

        // Forward once: index 1 (still < PlyCount=2)
        ui.NavigateForward();
        ui.Mode.ShouldBe(GameUIMode.Playback);

        // Forward again: index 2 >= PlyCount=2 → exits
        var (response, _) = ui.NavigateForward();
        ui.Mode.ShouldBe(GameUIMode.Playing);
        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
    }

    [Fact]
    public void NavigateForward_NotInPlayback_ReturnsNone()
    {
        var ui = CreateStandardUI();

        var (response, _) = ui.NavigateForward();

        response.ShouldBe(UIResponse.None);
    }

    [Fact]
    public void ExitPlayback_RestoresPlayingMode()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        var ui = CreateUI(game);

        ui.NavigateBack();
        ui.Mode.ShouldBe(GameUIMode.Playback);

        var (response, _) = ui.ExitPlayback();

        ui.Mode.ShouldBe(GameUIMode.Playing);
        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
    }

    [Fact]
    public void NavigateToPly_ValidIndex_EntersPlayback()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        game.TryMove(DoMove(D2, D4));
        var ui = CreateUI(game);

        var (response, _) = ui.NavigateToPly(1);

        ui.Mode.ShouldBe(GameUIMode.Playback);
        ui.PlaybackPlyIndex.ShouldBe(1);
        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
    }

    [Fact]
    public void NavigateToPly_InvalidIndex_ReturnsNone()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        var ui = CreateUI(game);

        var (response, _) = ui.NavigateToPly(5);

        response.ShouldBe(UIResponse.None);
        ui.Mode.ShouldBe(GameUIMode.Playing);
    }

    [Fact]
    public void Playback_DisplayBoard_ShowsHistoricalPosition()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        var ui = CreateUI(game);

        ui.NavigateToPly(0);

        // After ply 0 (e4), white pawn should be on E4, black pawn still on E7
        ui.DisplayBoard[E4].PieceType.ShouldBe(PieceType.Pawn);
        ui.DisplayBoard[E7].PieceType.ShouldBe(PieceType.Pawn);
        ui.DisplayBoard[E5].PieceType.ShouldBe(PieceType.None);
    }

    [Fact]
    public void Playback_TryPerformAction_IsIgnored()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        var ui = CreateUI(game);
        ui.NavigateBack();

        var (response, _) = ui.TryPerformAction(D2);

        response.ShouldBe(UIResponse.None);
    }

    // ── History scrolling ──────────────────────────────────────────

    [Fact]
    public void ScrollHistory_SetsScrollStart()
    {
        var game = new Game();
        // Play enough moves so moveCount > viewportRows
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        game.TryMove(DoMove(D2, D4));
        game.TryMove(DoMove(D7, D5));
        game.TryMove(DoMove(G1, F3));
        game.TryMove(DoMove(B8, C6));
        var ui = CreateUI(game);
        ui.HistoryViewportRows = 2; // 3 moves, 2 viewport rows → maxStart=1

        // Scroll up from auto (pinned to latest)
        var response = ui.ScrollHistory(-1);

        response.ShouldBe(UIResponse.IsUpdate);
        ui.HistoryScrollStart.ShouldBe(0);
    }

    [Fact]
    public void ScrollHistory_ScrollDownToEnd_ResetsToAuto()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        game.TryMove(DoMove(D2, D4));
        game.TryMove(DoMove(D7, D5));
        game.TryMove(DoMove(G1, F3));
        game.TryMove(DoMove(B8, C6));
        var ui = CreateUI(game);
        ui.HistoryViewportRows = 2;

        // Scroll up first
        ui.ScrollHistory(-1);
        ui.HistoryScrollStart.ShouldBe(0);

        // Scroll back down past the end → auto (null)
        ui.ScrollHistory(100);
        ui.HistoryScrollStart.ShouldBeNull();
    }

    // ── Setup mode ─────────────────────────────────────────────────

    [Fact]
    public void SetupSelect_SetsPendingPlacement()
    {
        var game = new Game(new Board(), Side.White, []);
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        var (response, _) = ui.SetupSelect(E4);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        response.HasFlag(UIResponse.NeedsPiecePlacement).ShouldBeTrue();
        ui.PendingPlacement.ShouldBe(E4);
        ui.Selected.ShouldBe(E4);
    }

    [Fact]
    public void TryPlacePiece_PlacesPieceOnBoard()
    {
        var game = new Game(new Board(), Side.White, []);
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.SetupSelect(E4);
        var (response, _) = ui.TryPlacePiece(E4, PieceType.Knight, Side.White);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        game.Board[E4].ShouldBe(new Piece(PieceType.Knight, Side.White));
        ui.PendingPlacement.ShouldBeNull();
    }

    [Fact]
    public void ClearSquare_RemovesPiece()
    {
        var game = new Game(new Board(), Side.White, []);
        game.SetPiece(E4, new Piece(PieceType.Knight, Side.White));
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.SetupSelect(E4);
        var (response, _) = ui.ClearSquare(E4);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        game.Board[E4].PieceType.ShouldBe(PieceType.None);
    }

    [Fact]
    public void CancelPlacement_ClearsPendingAndSelection()
    {
        var game = new Game(new Board(), Side.White, []);
        var ui = CreateUI(game);
        ui.IsSetupMode = true;
        ui.SetupSelect(E4);

        var (response, _) = ui.CancelPlacement();

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.PendingPlacement.ShouldBeNull();
        ui.Selected.ShouldBeNull();
    }

    [Fact]
    public void TogglePlacementSide_SwitchsSide()
    {
        var ui = CreateUI(new Game(new Board(), Side.White, []));
        ui.IsSetupMode = true;

        ui.PlacementSide.ShouldBe(Side.White);

        ui.TogglePlacementSide();

        ui.PlacementSide.ShouldBe(Side.Black);

        ui.TogglePlacementSide();

        ui.PlacementSide.ShouldBe(Side.White);
    }

    // ── Setup mode: pick up and drop ───────────────────────────────

    /// <summary>
    /// The use case the whole grammar exists for: a custom game started from the STANDARD board,
    /// with an opening nudged into shape. Two designations per piece, and neither square goes
    /// anywhere near the palette.
    /// </summary>
    [Fact]
    public void TrySetupAction_OccupiedThenEmpty_RelocatesWithoutOpeningThePalette()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        var (pickUp, _) = ui.TrySetupAction(E2);

        pickUp.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.PickedUp.ShouldBe(E2);
        ui.PendingPlacement.ShouldBeNull();

        var (drop, _) = ui.TrySetupAction(E4);

        drop.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        game.Board[E2].PieceType.ShouldBe(PieceType.None);
        game.Board[E4].ShouldBe(new Piece(PieceType.Pawn, Side.White));
        ui.PickedUp.ShouldBeNull();
        ui.PendingPlacement.ShouldBeNull();
    }

    /// <summary>
    /// Dropping onto an occupied square REPLACES the occupant, either colour — setting up a problem
    /// routinely means landing a piece where something else has to stop existing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrySetupAction_DropOnOccupiedSquare_ReplacesTheOccupant(bool occupantIsOpponent)
    {
        var game = new Game(new Board(), Side.White, []);
        game.SetPiece(D1, new Piece(PieceType.Queen, Side.White));
        game.SetPiece(D8, new Piece(PieceType.Rook, occupantIsOpponent ? Side.Black : Side.White));
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.TrySetupAction(D1);
        ui.TrySetupAction(D8);

        game.Board[D1].PieceType.ShouldBe(PieceType.None);
        game.Board[D8].ShouldBe(new Piece(PieceType.Queen, Side.White));
    }

    /// <summary>Relocation ignores the rules entirely — it never reaches Board.EvaluateAction.</summary>
    [Fact]
    public void TrySetupAction_RelocationIsNotAMove_AndNeedsNoLegality()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.TrySetupAction(B1); // knight
        ui.TrySetupAction(H6); // nothing legal about it

        game.Board[H6].ShouldBe(new Piece(PieceType.Knight, Side.White));
        game.PlyCount.ShouldBe(0);
    }

    [Fact]
    public void TrySetupAction_EmptySquare_OpensThePalette()
    {
        var game = new Game(new Board(), Side.White, []);
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        var (response, _) = ui.TrySetupAction(E4);

        response.HasFlag(UIResponse.NeedsPiecePlacement).ShouldBeTrue();
        ui.PendingPlacement.ShouldBe(E4);
        ui.PickedUp.ShouldBeNull();
    }

    /// <summary>Designating the square already in hand is how you reach the palette for an
    /// occupied square — to change its type, or to clear it with the red cross.</summary>
    [Fact]
    public void TrySetupAction_SameSquareTwice_OpensThePalette()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.TrySetupAction(E2);
        var (response, _) = ui.TrySetupAction(E2);

        response.HasFlag(UIResponse.NeedsPiecePlacement).ShouldBeTrue();
        ui.PendingPlacement.ShouldBe(E2);
        ui.PickedUp.ShouldBeNull();
        game.Board[E2].ShouldBe(new Piece(PieceType.Pawn, Side.White));
    }

    /// <summary>Del on a piece in hand removes it — the branch existed already but was
    /// unreachable, because a setup selection always opened the palette.</summary>
    [Fact]
    public void HandleKeyDown_SetupMode_DeleteWithPieceInHand_ClearsTheSquare()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.TrySetupAction(E2);
        ui.HandleKeyDown(InputKey.Delete, InputModifier.None);

        game.Board[E2].PieceType.ShouldBe(PieceType.None);
        ui.PickedUp.ShouldBeNull();
    }

    [Fact]
    public void HandleKeyDown_SetupMode_CoordinatesRelocate()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.HandleKeyDown(InputKey.G, InputModifier.None);
        ui.HandleKeyDown(InputKey.D1, InputModifier.None);
        ui.PickedUp.ShouldBe(G1);

        ui.HandleKeyDown(InputKey.F, InputModifier.None);
        ui.HandleKeyDown(InputKey.D3, InputModifier.None);

        game.Board[G1].PieceType.ShouldBe(PieceType.None);
        game.Board[F3].ShouldBe(new Piece(PieceType.Knight, Side.White));
    }

    /// <summary>
    /// The palette is drawn over a scrim spanning the WHOLE board, and its click handler used to
    /// swallow everything that missed the seven-square strip — so no square on the board responded
    /// while it was up, and Escape was the only way out. A click elsewhere now dismisses it and
    /// counts as a fresh designation.
    /// </summary>
    [Fact]
    public void HandleMouseDown_SetupMode_ClickOffThePalette_DismissesItAndRedesignates()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.SetupSelect(A1);
        ui.PendingPlacement.ShouldBe(A1);

        // H8 is a corner: the palette anchors on a1's column, seven squares wide, one row away.
        var target = ui.SquareRect(H8);
        var (response, clips) = ui.HandleMouseDown(target.UpperLeft.X + 5, target.UpperLeft.Y + 5);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        // The retired scrim invalidates the whole board, whatever the re-dispatch alone would need.
        clips.ShouldBeEmpty();
        ui.PendingPlacement.ShouldBeNull();
        ui.PickedUp.ShouldBe(H8);
    }

    /// <summary>
    /// The palette's render branch keys on PendingPlacement alone, not on the mode — so a pending
    /// square used to survive into the live game as a ghost popup (press s with it open), and a
    /// piece in hand as a phantom selection the first real click moved from.
    /// </summary>
    [Fact]
    public void LeavingSetupMode_DropsAnyHalfFinishedPlacement()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;
        ui.SetupSelect(E4);

        ui.HandleKeyDown(InputKey.S, InputModifier.None);

        ui.IsSetupMode.ShouldBeFalse();
        ui.PendingPlacement.ShouldBeNull();
        ui.Selected.ShouldBeNull();
    }

    [Fact]
    public void StatusLine_SetupMode_NamesThePieceInHand()
    {
        var game = new Game();
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        ui.StatusLine().ShouldContain("placing");

        ui.TrySetupAction(E2);

        ui.StatusLine().ShouldContain("Pawn");
        ui.StatusLine().ShouldContain("e2");
    }

    // ── Setup mode: drag (press, release elsewhere) ────────────────

    private static (int X, int Y) Centre(GameUI ui, Position square)
    {
        var rect = ui.SquareRect(square);
        return ((int)(rect.UpperLeft.X + rect.Width / 2), (int)(rect.UpperLeft.Y + rect.Height / 2));
    }

    private static GameUI SetupUI(Game game)
    {
        var ui = CreateUI(game);
        ui.IsSetupMode = true;
        return ui;
    }

    [Fact]
    public void HandlePointerUp_ReleaseOnAnotherSquare_CompletesTheDrag()
    {
        var game = new Game();
        var ui = SetupUI(game);

        var (dx, dy) = Centre(ui, D2);
        var (ux, uy) = Centre(ui, D4);
        ui.HandleMouseDown(dx, dy);
        ui.PickedUp.ShouldBe(D2);

        var (response, _) = ui.HandlePointerUp(ux, uy);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        game.Board[D2].PieceType.ShouldBe(PieceType.None);
        game.Board[D4].ShouldBe(new Piece(PieceType.Pawn, Side.White));
        ui.PickedUp.ShouldBeNull();
    }

    /// <summary>
    /// The release that ends a plain click must do nothing — the press already picked the piece up,
    /// and dispatching the same square again would re-open the palette on it. This is what keeps
    /// click-click and drag-drop one gesture instead of two models.
    /// </summary>
    [Fact]
    public void HandlePointerUp_ReleaseWhereItStarted_LeavesThePieceInHand()
    {
        var game = new Game();
        var ui = SetupUI(game);

        var (x, y) = Centre(ui, D2);
        ui.HandleMouseDown(x, y);
        var (response, _) = ui.HandlePointerUp(x, y);

        response.ShouldBe(UIResponse.None);
        ui.PickedUp.ShouldBe(D2);
        ui.PendingPlacement.ShouldBeNull();
        game.Board[D2].ShouldBe(new Piece(PieceType.Pawn, Side.White));
    }

    /// <summary>A press on an EMPTY square opens the palette; dragging off a modal is a cancel,
    /// not a place, so the release must not reach through it.</summary>
    [Fact]
    public void HandlePointerUp_AfterAPressThatOpenedThePalette_DoesNothing()
    {
        var game = new Game(new Board(), Side.White, []);
        var ui = SetupUI(game);

        var (dx, dy) = Centre(ui, D4);
        ui.HandleMouseDown(dx, dy);
        ui.PendingPlacement.ShouldBe(D4);

        var (ux, uy) = Centre(ui, F6);
        var (response, _) = ui.HandlePointerUp(ux, uy);

        response.ShouldBe(UIResponse.None);
        ui.PendingPlacement.ShouldBe(D4);
        game.Board[F6].PieceType.ShouldBe(PieceType.None);
    }

    /// <summary>Dragging off the board leaves the piece in hand rather than inventing a destination
    /// (or, worse, deleting it — a drag off the board is the classic "did I just lose that?").</summary>
    [Fact]
    public void HandlePointerUp_ReleaseOffTheBoard_LeavesThePieceInHand()
    {
        var game = new Game();
        var ui = SetupUI(game);

        var (dx, dy) = Centre(ui, D2);
        ui.HandleMouseDown(dx, dy);

        var (response, _) = ui.HandlePointerUp(0, 0);

        response.ShouldBe(UIResponse.None);
        ui.PickedUp.ShouldBe(D2);
        game.Board[D2].ShouldBe(new Piece(PieceType.Pawn, Side.White));
    }

    [Fact]
    public void HandlePointerUp_WithNoPressBeforeIt_DoesNothing()
    {
        var game = new Game();
        var ui = SetupUI(game);

        var (x, y) = Centre(ui, D4);
        var (response, _) = ui.HandlePointerUp(x, y);

        response.ShouldBe(UIResponse.None);
        game.PlyCount.ShouldBe(0);
    }

    /// <summary>A release during a real game is inert — the press committed the move already, and a
    /// second dispatch of the destination would select the piece that had just landed there.</summary>
    [Fact]
    public void HandlePointerUp_OutsideSetupMode_IsInert()
    {
        var game = new Game();
        var ui = CreateUI(game);

        var (dx, dy) = Centre(ui, E2);
        var (ux, uy) = Centre(ui, E4);
        ui.HandleMouseDown(dx, dy);
        var (response, _) = ui.HandlePointerUp(ux, uy);

        response.ShouldBe(UIResponse.None);
        ui.Selected.ShouldBe(E2);
        game.PlyCount.ShouldBe(0);
    }

    // ── Setup mode: the drag ghost ─────────────────────────────────

    /// <summary>Renders a UI to an offline surface, so a ghost is assertable without a window.</summary>
    private static RgbaImage RenderUI(GameUI ui)
    {
        const int size = 800;
        var renderer = new RgbaImageRenderer(size, size);
        ui.Render<RgbaImage, Renderer<RgbaImage>>(renderer,
            new RectInt(new PointInt(size, size), PointInt.Origin));
        return renderer.Surface;
    }

    /// <summary>Pixels inside <paramref name="region"/> where two renders disagree.</summary>
    private static int DifferingPixels(RgbaImage a, RgbaImage b, RectInt region)
    {
        var (x0, y0, x1, y1) = Clamp(a, region);

        var differing = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = (y * a.Width + x) * 4;
                if (a.Pixels[i] != b.Pixels[i] || a.Pixels[i + 1] != b.Pixels[i + 1]
                    || a.Pixels[i + 2] != b.Pixels[i + 2] || a.Pixels[i + 3] != b.Pixels[i + 3])
                {
                    differing++;
                }
            }
        }

        return differing;
    }

    /// <summary>Opaque pixels inside <paramref name="region"/>. The ALPHA channel is the only way to
    /// tell "never written" from "written in a colour that happens to match".</summary>
    private static int OpaquePixels(RgbaImage image, RectInt region)
    {
        var (x0, y0, x1, y1) = Clamp(image, region);

        var opaque = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (image.Pixels[(y * image.Width + x) * 4 + 3] == 0xff) opaque++;
            }
        }

        return opaque;
    }

    private static (int X0, int Y0, int X1, int Y1) Clamp(RgbaImage image, RectInt region) => (
        Math.Max(0, Math.Min(region.UpperLeft.X, region.LowerRight.X)),
        Math.Max(0, Math.Min(region.UpperLeft.Y, region.LowerRight.Y)),
        Math.Min(image.Width, Math.Max(region.UpperLeft.X, region.LowerRight.X)),
        Math.Min(image.Height, Math.Max(region.UpperLeft.Y, region.LowerRight.Y)));

    /// <summary>Picks the piece on <paramref name="square"/> up by pressing near its BOTTOM-RIGHT
    /// CORNER rather than its centre. Only an off-centre grab can catch a ghost that centres itself on
    /// the pointer, which is the jump the grab offset exists to prevent.</summary>
    private static (int X, int Y) PickUpOffCentre(GameUI ui, Position square)
    {
        var rect = ui.SquareRect(square);
        var x = rect.LowerRight.X - 4;
        var y = rect.LowerRight.Y - 4;
        ui.HandleMouseDown(x, y);
        return (x, y);
    }

    /// <summary>Motion with nothing in hand must not touch state. The GPU hosts deliver pointer motion
    /// whether or not a button is down, so this is the arm that stops a plain hover painting a ghost;
    /// the terminal is exempt by construction, but the gate lives here so all four hosts get it.</summary>
    [Fact]
    public void HandlePointerMove_WithNothingInHand_DoesNothing()
    {
        var ui = SetupUI(new Game());

        var (x, y) = Centre(ui, D4);
        var (response, clips) = ui.HandlePointerMove(x, y);

        response.ShouldBe(UIResponse.None);
        clips.ShouldBeEmpty();
        ui.DragPoint.ShouldBeNull();
        ui.GhostRect.ShouldBeNull();
    }

    /// <summary>A press picks the piece up but shows NO ghost: one appears only once the pointer moves,
    /// so a plain click never flashes one.</summary>
    [Fact]
    public void HandleMouseDown_PicksThePieceUpWithoutShowingAGhost()
    {
        var ui = SetupUI(new Game());

        PickUpOffCentre(ui, D2);

        ui.PickedUp.ShouldBe(D2);
        ui.DragPoint.ShouldBeNull();
        ui.GhostRect.ShouldBeNull();
    }

    [Fact]
    public void HandlePointerMove_HoldsThePieceAtTheOffsetItWasGrabbedBy()
    {
        var ui = SetupUI(new Game());
        var origin = ui.SquareRect(D2);
        var (grabX, grabY) = PickUpOffCentre(ui, D2);

        var (dx, dy) = Centre(ui, D5);
        var (response, clips) = ui.HandlePointerMove(dx, dy);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.GhostRect.ShouldNotBeNull();
        var ghost = ui.GhostRect!.Value;

        ghost.UpperLeft.X.ShouldBe(dx - (grabX - origin.UpperLeft.X));
        ghost.UpperLeft.Y.ShouldBe(dy - (grabY - origin.UpperLeft.Y));
        ghost.Width.ShouldBe(origin.Width);
        ghost.Height.ShouldBe(origin.Height);

        // First appearance damages the new footprint AND the origin square, just dimmed.
        clips.ShouldContain(ghost);
        clips.ShouldContain(origin);
    }

    /// <summary>A pointer that has not left its pixel must not cost a repaint — on the terminal every
    /// one of those is a partial sixel encode.</summary>
    [Fact]
    public void HandlePointerMove_ToTheSamePointAgain_ReportsNoDamage()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);

        var (response, clips) = ui.HandlePointerMove(dx, dy);

        response.ShouldBe(UIResponse.None);
        clips.ShouldBeEmpty();
        ui.GhostRect.ShouldNotBeNull();
    }

    /// <summary>Off the board the ghost hides rather than being carried over the history panel and the
    /// captured piles. The piece stays in hand, which is what a release out there already means.</summary>
    [Fact]
    public void HandlePointerMove_OffTheBoard_HidesTheGhostButKeepsThePieceInHand()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);
        var lastGhost = ui.GhostRect!.Value;

        var (response, clips) = ui.HandlePointerMove(0, 0);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.GhostRect.ShouldBeNull();
        ui.PickedUp.ShouldBe(D2);
        // The footprint it vacated, and the origin square whose dimming has just been undone.
        clips.ShouldContain(lastGhost);
        clips.ShouldContain(ui.SquareRect(D2));
    }

    /// <summary>
    /// The damage bound the whole cost model rests on: a one-square rect at an arbitrary offset
    /// straddles at most a 2x2 block, so a ghost that moved within one square dirties at most a 3x3
    /// one. It is a constraint to preserve rather than an observation — scaling the dragged piece up,
    /// the obvious touch-UI embellishment, silently makes it 4x4.
    /// </summary>
    [Fact]
    public void HandlePointerMove_ByOneStep_DamagesAtMostAThreeByThreeBlock()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var square = ui.SquareRect(D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);

        var (_, clips) = ui.HandlePointerMove(dx + 3, dy + 3);

        // Old and new footprint; the origin's dimming did not change, so it is not damaged again.
        clips.Length.ShouldBe(2);

        var left = clips.Min(r => Math.Min(r.UpperLeft.X, r.LowerRight.X));
        var top = clips.Min(r => Math.Min(r.UpperLeft.Y, r.LowerRight.Y));
        var right = clips.Max(r => Math.Max(r.UpperLeft.X, r.LowerRight.X));
        var bottom = clips.Max(r => Math.Max(r.UpperLeft.Y, r.LowerRight.Y));

        (right - left).ShouldBeLessThanOrEqualTo((int)square.Width * 3);
        (bottom - top).ShouldBeLessThanOrEqualTo((int)square.Height * 3);
    }

    /// <summary>
    /// The ghost is drawn where the cursor is, and the square it came from keeps a DIMMED copy rather
    /// than being emptied — an empty square under the picked-up tint reads as deleted rather than
    /// lifted, which matters here because Del genuinely does delete.
    /// </summary>
    [Fact]
    public void Render_WithAGhost_DrawsThePieceUnderTheCursorAndDimsItsOrigin()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var origin = ui.SquareRect(D2);
        var beforeMotion = RenderUI(ui);

        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);
        var ghost = ui.GhostRect!.Value;
        var withGhost = RenderUI(ui);

        // Something was drawn under the cursor, and it landed ON the surface rather than off it.
        DifferingPixels(beforeMotion, withGhost, ghost).ShouldBeGreaterThan(0);
        OpaquePixels(withGhost, ghost).ShouldBe((int)ghost.Width * (int)ghost.Height);

        // The origin changed — it no longer carries the full-strength piece ...
        DifferingPixels(beforeMotion, withGhost, origin).ShouldBeGreaterThan(0);

        // ... but it is not an empty square either: same tint, same everything, piece genuinely gone.
        var emptied = new Game();
        emptied.ClearPiece(D2);
        var withoutPiece = SetupUI(emptied);
        withoutPiece.SetupPickUp(D2);
        DifferingPixels(RenderUI(withoutPiece), withGhost, origin).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A resize mid-drag must carry BOTH halves. Selected — and so PickedUp — survives through the
    /// constructor, so dropping the drag point alone would leave a piece in hand with no ghost: an
    /// invisible drag, which is worse than no ghost at all. The offset rescales with the square.
    /// </summary>
    [Fact]
    public void Resize_PreservesADragInFlight()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);
        var grabbedBy = ui.GrabOffset;
        var squareBefore = ui.SquareRect(D2).Width;

        var resized = ui.Resize(1600, 1600);

        resized.PickedUp.ShouldBe(D2);
        resized.DragPoint.ShouldBe(ui.DragPoint);
        resized.GhostRect.ShouldNotBeNull();

        var squareAfter = resized.SquareRect(D2).Width;
        resized.GrabOffset.X.ShouldBe((int)(grabbedBy.X * squareAfter / squareBefore));
        resized.GrabOffset.Y.ShouldBe((int)(grabbedBy.Y * squareAfter / squareBefore));
    }

    /// <summary>The drag ends with the drop, without any exit having to remember to clear it: the ghost
    /// is read through PickedUp, so all four exits close it at once.</summary>
    [Fact]
    public void DragGhost_EndsWithTheDrop()
    {
        var game = new Game();
        var ui = SetupUI(game);
        PickUpOffCentre(ui, D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);
        ui.GhostRect.ShouldNotBeNull();

        ui.HandlePointerUp(dx, dy);

        ui.PickedUp.ShouldBeNull();
        ui.DragPoint.ShouldBeNull();
        ui.GhostRect.ShouldBeNull();
        game.Board[D5].ShouldBe(new Piece(PieceType.Pawn, Side.White));
    }

    [Fact]
    public void DragGhost_EndsWhenSetupModeIsLeft()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);

        ui.IsSetupMode = false;

        ui.DragPoint.ShouldBeNull();
        ui.GhostRect.ShouldBeNull();
    }

    // ── Last-move arrow: the knight's L ────────────────────────────

    /// <summary>The last-move arrow's green is the only green on the board — every square fill and
    /// piece colour has red at or above green — so "is this pixel arrow ink" needs no baseline
    /// render to compare against.</summary>
    private static bool IsArrowInk(RgbaImage image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return image.Pixels[i + 1] > image.Pixels[i] + 20 && image.Pixels[i + 1] > image.Pixels[i + 2] + 20;
    }

    private static GameUI AfterMove(Position from, Position to)
    {
        var game = new Game();
        game.TryMove(DoMove(from, to)).ShouldBe(ActionResult.Move);
        return CreateUI(game);
    }

    /// <summary>
    /// A knight moves two along a rank or file and then one across, so the arrow does too: long leg
    /// first, up the b file to b3, then across to c3.
    ///
    /// <para>Drawn on a board holding nothing but the knight, because arrows are painted UNDER the
    /// pieces — on a standard board the pawn on b2 sits on top of the long leg and the test would be
    /// asserting the pawn's colour. An <see cref="GameUI.ExplicitArrows"/> arrow takes the same
    /// <c>DrawLastMoveArrow</c> path as a real last move, which is also how the chess-mcp puzzle
    /// diagrams reach it.</para>
    /// </summary>
    [Fact]
    public void Render_KnightArrow_DrawsAnLRatherThanADiagonal()
    {
        var game = new Game(new Board(), Side.White, []);
        game.SetPiece(B1, new Piece(PieceType.Knight, Side.White));
        var ui = CreateUI(game);
        ui.ExplicitArrows = [(B1, C3, false)];

        var image = RenderUI(ui);

        var (bx, b1y) = Centre(ui, B1);
        var (cx, c3y) = Centre(ui, C3);

        // The long leg runs up the b file, so b2's centre is on it.
        var (b2x, b2y) = Centre(ui, B2);
        IsArrowInk(image, b2x, b2y).ShouldBeTrue("the long leg should run up the b file through b2");

        // The short leg runs along rank 3, so the point between b3 and c3 is on it.
        IsArrowInk(image, (bx + cx) / 2, c3y).ShouldBeTrue("the short leg should run along rank 3");

        // And the straight diagonal — the old arrow — is now empty.
        IsArrowInk(image, (bx + cx) / 2, (b1y + c3y) / 2)
            .ShouldBeFalse("no ink belongs on the diagonal the knight cannot travel");
    }

    /// <summary>The real last-move path, not just <see cref="GameUI.ExplicitArrows"/>: after 1.Nc3 the
    /// diagonal b1–c3 carries no arrow.</summary>
    [Fact]
    public void Render_KnightLastMove_LeavesTheDiagonalEmpty()
    {
        var ui = AfterMove(B1, C3);
        var image = RenderUI(ui);

        var (bx, b1y) = Centre(ui, B1);
        var (cx, c3y) = Centre(ui, C3);

        IsArrowInk(image, (bx + cx) / 2, (b1y + c3y) / 2).ShouldBeFalse();
    }

    /// <summary>The corner is filled once rather than by two overlapping legs: the arrow colour is
    /// translucent, so an overlap blends twice and leaves a darker blob exactly where a reader looks
    /// to see which way the piece turned.</summary>
    [Fact]
    public void Render_KnightMove_JoinsTheLegsWithoutAGapAtTheCorner()
    {
        var ui = AfterMove(B1, C3);
        var image = RenderUI(ui);

        var (bx, _) = Centre(ui, B1);
        var (_, c3y) = Centre(ui, C3);

        IsArrowInk(image, bx, c3y).ShouldBeTrue("the corner of the L must be inked");
    }

    /// <summary>Every other piece really does travel in a straight line, so its arrow was already
    /// telling the truth and must keep doing so.</summary>
    [Fact]
    public void Render_NonKnightMove_KeepsTheStraightArrow()
    {
        var ui = AfterMove(E2, E4);
        var image = RenderUI(ui);

        var (ex, e2y) = Centre(ui, E2);
        var (_, e4y) = Centre(ui, E4);

        IsArrowInk(image, ex, (e2y + e4y) / 2).ShouldBeTrue("a pawn's arrow runs straight up the file");
    }

    /// <summary>A fresh grab must not inherit the last drag's pointer position, which would paint a
    /// ghost across the board before the pointer had moved at all.</summary>
    [Fact]
    public void HandleMouseDown_AfterAnEarlierDrag_ShowsNoGhostUntilThePointerMovesAgain()
    {
        var ui = SetupUI(new Game());
        PickUpOffCentre(ui, D2);
        var (dx, dy) = Centre(ui, D5);
        ui.HandlePointerMove(dx, dy);
        ui.HandlePointerUp(dx, dy);

        PickUpOffCentre(ui, E2);

        ui.PickedUp.ShouldBe(E2);
        ui.DragPoint.ShouldBeNull();
        ui.GhostRect.ShouldBeNull();
    }

    /// <summary>
    /// Setup mode is the one place the board can hold more pieces than a legal army, and RenderBoard's
    /// piece buffer was sized 32 — so a custom game started from the STANDARD board (already 32 pieces)
    /// died on the first placement onto an empty square. Reported against Chess.GUI: replacing the b2
    /// pawn with a bishop was fine (no net change), adding a pawn on b3 overflowed and closed the window.
    /// Rendering is the only way to catch it — every piece of state was correct.
    /// </summary>
    [Fact]
    public void Render_SetupModeAddingPieceBeyondAFullArmy_DoesNotOverflow()
    {
        var game = new Game();
        game.SetPiece(B2, new Piece(PieceType.Bishop, Side.White)); // replace: still 32
        game.SetPiece(B3, new Piece(PieceType.Pawn, Side.White));   // add: 33 — the crash

        Should.NotThrow(() => RenderToImage(game));
    }

    /// <summary>The buffer's real bound: every square occupied, which setup mode permits.</summary>
    [Fact]
    public void Render_SetupModeWithEverySquareOccupied_DoesNotOverflow()
    {
        var game = new Game();
        foreach (var position in AllPositions())
            game.SetPiece(position, new Piece(PieceType.Queen, Side.White));

        Should.NotThrow(() => RenderToImage(game));
    }

    private static void RenderToImage(Game game)
    {
        const int size = 800;
        var renderer = new RgbaImageRenderer(size, size);
        var ui = CreateUI(game);
        ui.IsSetupMode = true;
        ui.Render<RgbaImage, Renderer<RgbaImage>>(renderer,
            new RectInt(new PointInt(size, size), PointInt.Origin));
    }

    // ── Keymap toggle ──────────────────────────────────────────────

    [Fact]
    public void ToggleKeymap_TogglesState()
    {
        var ui = CreateStandardUI();

        ui.ShowingKeymap.ShouldBeFalse();

        var (response1, _) = ui.ToggleKeymap();
        ui.ShowingKeymap.ShouldBeTrue();
        response1.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();

        var (response2, _) = ui.ToggleKeymap();
        ui.ShowingKeymap.ShouldBeFalse();
        response2.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
    }

    // ── Resize ─────────────────────────────────────────────────────

    [Fact]
    public void Resize_PreservesGameState()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        var ui = CreateUI(game);
        ui.TryPerformAction(D7);
        ui.ShowingKeymap = true;

        var resized = ui.Resize(1024, 768);

        resized.Game.ShouldBeSameAs(game);
        resized.ShowingKeymap.ShouldBeTrue();
    }

    // ── FindSelected (pixel hit testing) ───────────────────────────

    [Fact]
    public void FindSelected_InsideBoard_ReturnsPosition()
    {
        var ui = CreateStandardUI();
        var rect = ui.SquareRect(E4);
        var centerX = (rect.UpperLeft.X + rect.LowerRight.X) / 2;
        var centerY = (rect.UpperLeft.Y + rect.LowerRight.Y) / 2;

        var pos = ui.FindSelected(centerX, centerY);

        pos.ShouldBe(E4);
    }

    [Fact]
    public void FindSelected_OutsideBoard_ReturnsNull()
    {
        var ui = CreateStandardUI();

        var pos = ui.FindSelected(0, 0);

        pos.ShouldBeNull();
    }

    // ── Board flip (orientation) ───────────────────────────────────

    [Fact]
    public void FlipBoard_MirrorsSquareToOppositeCorner()
    {
        var normal = CreateStandardUI();
        var flipped = CreateStandardUI();
        flipped.FlipBoard = true;

        // A 180° rotation: a1 (bottom-left) lands where h8 (top-right) sits unflipped, and each
        // square maps to its file+rank mirror (e2 -> d7).
        flipped.SquareRect(A1).ShouldBe(normal.SquareRect(H8));
        flipped.SquareRect(H8).ShouldBe(normal.SquareRect(A1));
        flipped.SquareRect(E2).ShouldBe(normal.SquareRect(D7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindSelected_RoundTripsSquareRectCenter_EitherOrientation(bool flip)
    {
        var ui = CreateStandardUI();
        ui.FlipBoard = flip;

        // Draw and hit-test share one mapping, so a tap at a square's centre must resolve to it
        // regardless of orientation.
        foreach (var pos in new[] { A1, H8, E2, E4, D7, G1 })
        {
            var rect = ui.SquareRect(pos);
            var cx = (rect.UpperLeft.X + rect.LowerRight.X) / 2;
            var cy = (rect.UpperLeft.Y + rect.LowerRight.Y) / 2;
            ui.FindSelected(cx, cy).ShouldBe(pos);
        }
    }

    [Fact]
    public void CtrlF_TogglesFlipBoard()
    {
        var ui = CreateStandardUI();
        ui.FlipBoard.ShouldBeFalse();

        var (response, _) = ui.HandleKeyDown(InputKey.F, InputModifier.Ctrl);

        ui.FlipBoard.ShouldBeTrue();
        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();

        ui.HandleKeyDown(InputKey.F, InputModifier.Ctrl);
        ui.FlipBoard.ShouldBeFalse();
    }

    [Fact]
    public void BareF_SelectsFileF_DoesNotFlip()
    {
        var ui = CreateStandardUI();

        ui.HandleKeyDown(InputKey.F, InputModifier.None);

        ui.FlipBoard.ShouldBeFalse();     // bare f must NOT flip (it's the file selector)
        ui.PendingFile.ShouldBe(File.F);
    }

    [Fact]
    public void FlipBoard_PreservedAcrossResize()
    {
        var ui = CreateStandardUI();
        ui.FlipBoard = true;

        ui.Resize(1000, 1000).FlipBoard.ShouldBeTrue();
    }

    // ── LastMove ───────────────────────────────────────────────────

    [Fact]
    public void LastMove_NoMoves_IsNull()
    {
        var ui = CreateStandardUI();

        ui.LastMove.ShouldBeNull();
    }

    [Fact]
    public void LastMove_AfterMove_ReturnsDestination()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        var ui = CreateUI(game);

        ui.LastMove.ShouldNotBeNull();
        ui.LastMove.Value.To.ShouldBe(E4);
        ui.LastMove.Value.IsCapture.ShouldBeFalse();
    }

    [Fact]
    public void LastMove_Playback_ReturnsPlaybackPly()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        game.TryMove(DoMove(D2, D4));
        var ui = CreateUI(game);

        ui.NavigateToPly(0);

        ui.LastMove.ShouldNotBeNull();
        ui.LastMove.Value.To.ShouldBe(E4);
    }

    // ── HandleKeyDown ────────────────────────────────────────────

    [Fact]
    public void HandleKeyDown_FileKey_SetsPendingFile()
    {
        var ui = CreateStandardUI();

        var (response, _) = ui.HandleKeyDown(InputKey.E, InputModifier.None);

        response.ShouldBe(UIResponse.IsUpdate);
        ui.PendingFile.ShouldBe(File.E);
    }

    [Fact]
    public void HandleKeyDown_FileAndRankKeys_PerformsMove()
    {
        var ui = CreateStandardUI();

        ui.HandleKeyDown(InputKey.E, InputModifier.None);
        var (response, _) = ui.HandleKeyDown(InputKey.D4, InputModifier.None);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Game.Board[E4].PieceType.ShouldBe(PieceType.Pawn);
        ui.PendingFile.ShouldBeNull();
    }

    [Fact]
    public void HandleKeyDown_F9_ReturnsNeedsReset()
    {
        var ui = CreateStandardUI();

        var (response, _) = ui.HandleKeyDown(InputKey.F9, InputModifier.None);

        response.ShouldBe(UIResponse.NeedsReset);
    }

    [Fact]
    public void HandleKeyDown_EscapeWithSelection_ClearsSelection()
    {
        var ui = CreateStandardUI();
        ui.TryPerformAction(E2); // select e2
        ui.Selected.ShouldBe(E2);

        var (response, _) = ui.HandleKeyDown(InputKey.Escape, InputModifier.None);

        response.HasFlag(UIResponse.IsUpdate).ShouldBeTrue();
        ui.Selected.ShouldBeNull();
    }

    [Fact]
    public void HandleKeyDown_EscapeWithPendingFile_CancelsWithoutLeavingGame()
    {
        var ui = CreateStandardUI();
        ui.HandleKeyDown(InputKey.E, InputModifier.None); // pending file e, nothing selected yet
        ui.PendingFile.ShouldBe(File.E);

        var (response, _) = ui.HandleKeyDown(InputKey.Escape, InputModifier.None);

        // First escape cancels the pending input; it must NOT fall through to the menu.
        ui.PendingFile.ShouldBeNull();
        response.HasFlag(UIResponse.NeedsRestart).ShouldBeFalse();
    }

    [Fact]
    public void HandleKeyDown_EscapeWithNothingSelected_RequestsBackToMenu()
    {
        var ui = CreateStandardUI();
        ui.Selected.ShouldBeNull();
        ui.PendingFile.ShouldBeNull();

        var (response, _) = ui.HandleKeyDown(InputKey.Escape, InputModifier.None);

        // Progressive escape: with nothing to cancel, escape unwinds one level to the menu.
        response.HasFlag(UIResponse.NeedsRestart).ShouldBeTrue();
    }

    [Fact]
    public void HandleKeyDown_CtrlLeft_EntersPlayback()
    {
        var ui = CreateStandardUI();
        // Make a move first so there's history
        ui.TryPerformAction(E2);
        ui.TryPerformAction(E4);

        var (response, _) = ui.HandleKeyDown(InputKey.Left, InputModifier.Ctrl);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Mode.ShouldBe(GameUIMode.Playback);
    }

    [Fact]
    public void HandleKeyDown_F1_TogglesKeymap()
    {
        var ui = CreateStandardUI();
        ui.ShowingKeymap.ShouldBeFalse();

        ui.HandleKeyDown(InputKey.F1, InputModifier.None);

        ui.ShowingKeymap.ShouldBeTrue();
    }

    [Fact]
    public void HandleKeyDown_SetupMode_PlacesPiece()
    {
        var game = new Game(new Board(), Side.White, []);
        var ui = CreateUI(game);
        ui.IsSetupMode = true;

        // Select square e4
        ui.HandleKeyDown(InputKey.E, InputModifier.None);
        ui.HandleKeyDown(InputKey.D4, InputModifier.None);
        ui.PendingPlacement.ShouldBe(E4);

        // Place a queen
        var (response, _) = ui.HandleKeyDown(InputKey.Q, InputModifier.None);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Game.Board[E4].PieceType.ShouldBe(PieceType.Queen);
    }

    // ── HandleMouseDown ──────────────────────────────────────────

    [Fact]
    public void HandleMouseDown_ClickSquare_SelectsOrMoves()
    {
        var ui = CreateStandardUI();

        // Click on E4 area — should auto-move pawn
        var (response, _) = ui.HandleMouseDown(
            ui.SquareRect(E4).UpperLeft.X + 5,
            ui.SquareRect(E4).UpperLeft.Y + 5);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Game.Board[E4].PieceType.ShouldBe(PieceType.Pawn);
    }

    // ── HandleMouseWheel ─────────────────────────────────────────

    [Fact]
    public void HandleMouseWheel_ScrollsHistory()
    {
        var ui = CreateStandardUI();
        ui.HistoryViewportRows = 10;

        // Make a move for history
        ui.TryPerformAction(E2);
        ui.TryPerformAction(E4);

        var (response, _) = ui.HandleMouseWheel(-1);

        // ScrollHistory should return a valid response
        (response == UIResponse.None || response.HasFlag(UIResponse.IsUpdate)).ShouldBeTrue();
    }

    // ── MoveLockSide (Play by Link) ───────────────────────────────

    [Fact]
    public void MoveLockSide_Null_AllowsHotSeatMoves()
    {
        var ui = CreateStandardUI();

        ui.TryPerformAction(E2);
        ui.TryPerformAction(E4);
        ui.TryPerformAction(E7);
        ui.TryPerformAction(E5);

        ui.Game.PlyCount.ShouldBe(2); // both colours moved through one UI, as ever
    }

    [Fact]
    public void MoveLockSide_MatchingSide_AllowsMove()
    {
        var ui = CreateStandardUI();
        ui.MoveLockSide = Side.White;

        ui.TryPerformAction(E2);
        var (response, _) = ui.TryPerformAction(E4);

        response.HasFlag(UIResponse.IsUpdate).ShouldBeTrue();
        ui.Game.PlyCount.ShouldBe(1);
    }

    [Fact]
    public void MoveLockSide_OtherSidesTurn_BlocksSelectionAndMove()
    {
        var ui = CreateStandardUI();
        ui.MoveLockSide = Side.Black; // White to move — this tab controls Black

        var (selectResponse, _) = ui.TryPerformAction(E2);
        selectResponse.ShouldBe(UIResponse.None);
        ui.Selected.ShouldBeNull(); // a locked click does nothing at all — no selection

        var (moveResponse, _) = ui.TryPerformAction(DoMove(E2, E4));
        moveResponse.ShouldBe(UIResponse.None);
        ui.Game.PlyCount.ShouldBe(0);
    }

    [Fact]
    public void MoveLockSide_SelfLocksAfterOneMove()
    {
        // The correspondence gate: CurrentSide flips on commit, so one property set at load
        // yields exactly one local move.
        var ui = CreateStandardUI();
        ui.MoveLockSide = Side.White;

        ui.TryPerformAction(E2);
        ui.TryPerformAction(E4);
        ui.Game.PlyCount.ShouldBe(1);

        ui.TryPerformAction(E7); // Black's reply must not be playable from this tab
        ui.TryPerformAction(E5);

        ui.Game.PlyCount.ShouldBe(1);
        ui.Selected.ShouldBeNull();
    }

    [Fact]
    public void MoveLockSide_PromotionAction_IsBlockedToo()
    {
        // The Action overload is the promotion picker's path — it must honor the lock as well.
        // White pawn teleported to a7, promotion square cleared, Black to move.
        var game = new Game(Board.StandardBoard - A7 - A8 - B8 + DoMove(A2, A7), Side.Black, []);
        var ui = CreateUI(game);
        ui.MoveLockSide = Side.White; // Black to move — the White promotion must stay locked

        var (response, _) = ui.TryPerformAction(Promote(A7, A8, PieceType.Queen));

        response.ShouldBe(UIResponse.None);
        ui.Game.PlyCount.ShouldBe(0);
    }

    [Fact]
    public void MoveLockSide_PlaybackNavigation_StillWorksWhileLocked()
    {
        var game = new Game();
        game.TryMove(DoMove(E2, E4));
        game.TryMove(DoMove(E7, E5));
        var ui = CreateUI(game);
        ui.MoveLockSide = Side.Black; // White to move — board locked for this tab

        var (response, _) = ui.NavigateBack();

        response.HasFlag(UIResponse.IsUpdate).ShouldBeTrue();
        ui.Mode.ShouldBe(GameUIMode.Playback); // reviewing history is never gated
    }

    [Fact]
    public void Resize_PreservesMoveLockSide()
    {
        // The web host rebuilds GameUI via Resize on every canvas metrics change — losing the
        // lock there would silently unlock the board mid-link-game.
        var ui = CreateStandardUI();
        ui.MoveLockSide = Side.Black;

        var resized = ui.Resize(1024, 768);

        resized.MoveLockSide.ShouldBe(Side.Black);
    }

    // ── Reserved palette colours ───────────────────────────────────

    [Fact]
    public void ReservedPaletteColors_AreOpaqueUniqueAndIncludeTheCaptureViolet()
    {
        var colors = GameUI.ReservedPaletteColors;

        // A translucent constant never reaches the encoder — it composites into per-background
        // blends first — so reserving one would hold a palette slot for a colour no pixel carries.
        colors.ShouldAllBe(c => c.Alpha == 0xff, "reserved colours must be opaque");

        // A duplicate quietly burns one of Sixel's 255 slots. A forward static-field reference would
        // show up here too: it reads as transparent black, colliding with the real background entry.
        colors.Distinct().Count().ShouldBe(colors.Length, "reserved colours must be unique");

        // The accent that prompted the reservation feature (Console.Lib 4.2): the violet capture
        // marker, which used to snap to board colours when it lost the frequency cut.
        colors.ShouldContain(new RGBAColor32(0x8A, 0x4F, 0xD0, 0xff));
    }
}
