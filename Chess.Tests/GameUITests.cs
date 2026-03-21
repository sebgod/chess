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

    // ── Selection ──────────────────────────────────────────────────

    [Fact]
    public void Select_ValidPiece_SetsSelected()
    {
        var ui = CreateStandardUI();

        var (response, clips) = ui.TryPerformAction(E2);

        response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();
        ui.Selected.ShouldBe(E2);
        clips.ShouldContain(ui.SquareRect(E2));
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
        clips.ShouldContain(ui.SquareRect(E2));
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
}
