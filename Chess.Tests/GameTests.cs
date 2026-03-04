using Chess.Lib;
using Shouldly;
using System.Collections.Immutable;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.ActionResult;
using static Chess.Lib.GameStatus;
using static Chess.Lib.PieceType;
using static Chess.Lib.Position;
using static Chess.Lib.Side;

namespace Chess.Tests;

public class GameTests
{
    [Theory]
    [MemberData(nameof(DataSource))]
    public void EvaluateMoves(Game game, Lib.Action action, ActionResult expectedResult, Board? expectedBoard, GameStatus expectedStatus, PieceType expectedCapture, PieceType expectedPromotion)
    {
        var copy = game.Board;
        var ((result, status), newBoard, pliesAfter) = game.Board.EvaluateAction(game.Plies, action);
        game.Board.ShouldBe(copy); // board stays unchanged
        result.ShouldBe(expectedResult);
        newBoard.ShouldBe(expectedBoard ?? copy);
        status.ShouldBe(expectedStatus);
        pliesAfter.LastOrDefault().Captured.ShouldBe(expectedCapture);
        pliesAfter.LastOrDefault().Promoted.ShouldBe(expectedPromotion);
    }

    public static IEnumerable<object[]> DataSource() => [
        Custom(
            new Board {
                [B8] = (Black, King), [H8] = (Black, Rook),
                [B7] = (Black, Pawn), [C7] = (Black, Pawn),
                [H6] = (Black, Rook),
                [E4] = (White, Pawn), [F4] = (Black, Bishop),
                [F3] = (White, Pawn), [G3] = (Black, Pawn),
                [A2] = (White, Rook), [B2] = (White, Pawn), [G2] = (White, Pawn),
                [A1] = (White, Rook), [G1] = (White, King)
            },
            Black,
            [],
            DoMove(H6, H1),
            Move,
            new Board {
                [B8] = (Black, King), [H8] = (Black, Rook),
                [B7] = (Black, Pawn), [C7] = (Black, Pawn),
                [E4] = (White, Pawn), [F4] = (Black, Bishop),
                [F3] = (White, Pawn), [G3] = (Black, Pawn),
                [A2] = (White, Rook), [B2] = (White, Pawn), [G2] = (White, Pawn),
                [A1] = (White, Rook), [G1] = (White, King), [H1] = (Black, Rook)
            },
            Checkmate
        ),
        Custom(
            new Board {
                [E8] = (Black, King),
                [G7] = (Black, Pawn),
                [F5] = (Black, Bishop), [G5] = (White, Pawn), [H5] = (White, King),
                [E4] = (Black, Queen),
                [G1] = (White, Rook)
            },
            Black,
            [],
            DoMove(F5, G6),
            Move,
            new Board {
                [E8] = (Black, King),
                [G7] = (Black, Pawn),
                [G6] = (Black, Bishop),
                [G5] = (White, Pawn), [H5] = (White, King),
                [E4] = (Black, Queen),
                [G1] = (White, Rook)
            },
            Checkmate
        ),
        Custom(
            new Board {
                [A8] = (Black, Rook), [E8] = (Black, King),  [H8] = (Black, Rook),
                [F4] = (White, Queen),
                [E1] = (White, King)
            },
            Black,
            [],
            DoMove(E8, C8),
            Castling,
            new Board {
                [C8] = (Black, King), [D8] = (Black, Rook), [H8] = (Black, Rook),
                [F4] = (White, Queen),
                [E1] = (White, King)
            },
            Ongoing
        ),
        Custom(
            new Board {
                [A8] = (Black, Rook),
                [E8] = (Black, King),
                [H8] = (Black, Rook),
                [F4] = (White, Queen),
                [E1] = (White, King)
            },
            Black,
            [],
            DoMove(E8, G8),
            Impossible,
            default,
            Ongoing
        ),
        Custom(
            Board.StandardBoard + DoMove(H2, H5) + DoMove(G7, G5),
            White,
            [
                new RecordedPly(H2, H5, Move, Pawn),
                new RecordedPly(G7, G5, Move, Pawn)
            ],
            DoMove(H5, G6),
            EnPassant,
            Board.StandardBoard + DoMove(H2, G6) - G7,
            Ongoing,
            Pawn
        ),
        Custom(
            new Board { [A7] = (White, Pawn), [H7] = (Black, King), [D3] = (White, King) },
            White,
            [],
            Promote(A7, A8, Queen), Promotion,
            new Board { [A8] = (White, Queen), [H7] = (Black, King), [D3] = (White, King) },
            Ongoing,
            PieceType.None,
            Queen
        ),
        FromPlies([], new RecordedPly(A2, A3, Move, Pawn)),
        FromPlies([], new RecordedPly(D7, D5, Move, Pawn)),
        FromPlies([], new RecordedPly(B1, A3, Move, Knight)),
        FromPlies([
            new RecordedPly(E2, E4, Move, Pawn), new RecordedPly(G7, G5, Move, Pawn),
            new RecordedPly(B1, C3, Move, Knight), new RecordedPly(F7, F5, Move, Pawn)
        ], new RecordedPly(D1, H5, Move, Queen, Status: Checkmate))
    ];

    public static object[] FromPlies(ImmutableList<RecordedPly> plies, RecordedPly move)
    {
        var game = Game.FromReplay(plies);
        var action = DoMove(move.From, move.To);

        return Custom(
            game,
            action,
            move.Result,
            game.Board + action,
            move.Status,
            move.Captured,
            move.Promoted
        );
    }

    public static object[] Custom(
        Board board,
        Side side,
        ImmutableList<RecordedPly> plies,
        Lib.Action move,
        ActionResult expectedResult,
        Board? expectedBoard,
        GameStatus expectedStatus,
        PieceType captured = PieceType.None,
        PieceType promoted = PieceType.None)
        => Custom(new Game(board, side, plies), move, expectedResult, expectedBoard, expectedStatus, captured, promoted);

    public static object[] Custom(
        Game game,
        Lib.Action move,
        ActionResult expectedResult,
        Board? expectedBoard,
        GameStatus expectedStatus,
        PieceType captured,
        PieceType promoted)
    => [game, move, expectedResult, expectedBoard!, expectedStatus, captured, promoted];
}
