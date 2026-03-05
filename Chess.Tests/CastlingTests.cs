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

public class CastlingTests
{
    [Theory]
    [MemberData(nameof(DataSource))]
    public void EvaluateCastling(
        Game game,
        Lib.Action action,
        ActionResult expectedResult,
        Board? expectedBoard,
        GameStatus expectedStatus,
        PieceType expectedCapture,
        PieceType expectedPromotion)
    {
        var copy = game.Board;
        var ((result, status), newBoard, pliesAfter) = game.Board.EvaluateAction(game.Plies, action);
        game.Board.ShouldBe(copy);
        result.ShouldBe(expectedResult);
        newBoard.ShouldBe(expectedBoard ?? copy);
        status.ShouldBe(expectedStatus);
        pliesAfter.LastOrDefault().Captured.ShouldBe(expectedCapture);
        pliesAfter.LastOrDefault().Promoted.ShouldBe(expectedPromotion);
    }

    public static IEnumerable<object[]> DataSource() => [
        // ── White Kingside Castling ──────────────────────────────────

        // Valid: clear path, no attacks, no prior moves
        Custom(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Castling,
            new Board {
                [G1] = (White, King), [F1] = (White, Rook),
                [E8] = (Black, King)
            },
            Ongoing
        ),

        // Blocked: knight on F1
        Custom(
            new Board {
                [E1] = (White, King), [F1] = (White, Knight), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // Blocked: bishop on G1
        Custom(
            new Board {
                [E1] = (White, King), [G1] = (White, Bishop), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // Attacked: opponent rook controls F1
        Custom(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook),
                [F8] = (Black, Rook), [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // Attacked: opponent rook controls G1
        Custom(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook),
                [G8] = (Black, Rook), [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // In check: king on E1 attacked — cannot castle out of check
        Custom(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook),
                [E8] = (Black, Rook), [A8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // King has moved previously: castling forbidden
        Custom(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White,
            [
                new RecordedPly(E1, F1, Move, King),
                new RecordedPly(E8, D8, Move, King),
                new RecordedPly(F1, E1, Move, King),
                new RecordedPly(D8, E8, Move, King)
            ],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // H-rook has moved previously: kingside castling forbidden
        Custom(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White,
            [
                new RecordedPly(H1, H3, Move, Rook),
                new RecordedPly(E8, D8, Move, King),
                new RecordedPly(H3, H1, Move, Rook),
                new RecordedPly(D8, E8, Move, King)
            ],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // A-rook moved, but kingside should still be allowed
        Custom(
            new Board {
                [A1] = (White, Rook), [E1] = (White, King), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White,
            [
                new RecordedPly(A1, A3, Move, Rook),
                new RecordedPly(E8, D8, Move, King),
                new RecordedPly(A3, A1, Move, Rook),
                new RecordedPly(D8, E8, Move, King)
            ],
            DoMove(E1, G1),
            Castling,
            new Board {
                [A1] = (White, Rook), [G1] = (White, King), [F1] = (White, Rook),
                [E8] = (Black, King)
            },
            Ongoing
        ),

        // ── White Queenside Castling ─────────────────────────────────

        // Valid: clear path, no attacks, no prior moves
        Custom(
            new Board {
                [A1] = (White, Rook), [E1] = (White, King),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, C1),
            Castling,
            new Board {
                [C1] = (White, King), [D1] = (White, Rook),
                [E8] = (Black, King)
            },
            Ongoing
        ),

        // Blocked: bishop on D1
        Custom(
            new Board {
                [A1] = (White, Rook), [D1] = (White, Bishop), [E1] = (White, King),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, C1),
            Impossible,
            default,
            Ongoing
        ),

        // Blocked: knight on C1
        Custom(
            new Board {
                [A1] = (White, Rook), [C1] = (White, Knight), [E1] = (White, King),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, C1),
            Impossible,
            default,
            Ongoing
        ),

        // B1 occupied: queenside castling should still be legal (B1 not in king's path)
        Custom(
            new Board {
                [A1] = (White, Rook), [B1] = (White, Knight), [E1] = (White, King),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, C1),
            Castling,
            new Board {
                [B1] = (White, Knight), [C1] = (White, King), [D1] = (White, Rook),
                [E8] = (Black, King)
            },
            Ongoing
        ),

        // Attacked: opponent rook controls D1
        Custom(
            new Board {
                [A1] = (White, Rook), [E1] = (White, King),
                [D8] = (Black, Rook), [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, C1),
            Impossible,
            default,
            Ongoing
        ),

        // A-rook has moved: queenside castling forbidden
        Custom(
            new Board {
                [A1] = (White, Rook), [E1] = (White, King),
                [E8] = (Black, King)
            },
            White,
            [
                new RecordedPly(A1, A3, Move, Rook),
                new RecordedPly(E8, D8, Move, King),
                new RecordedPly(A3, A1, Move, Rook),
                new RecordedPly(D8, E8, Move, King)
            ],
            DoMove(E1, C1),
            Impossible,
            default,
            Ongoing
        ),

        // H-rook moved, but queenside should still be allowed
        Custom(
            new Board {
                [A1] = (White, Rook), [E1] = (White, King), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            White,
            [
                new RecordedPly(H1, H3, Move, Rook),
                new RecordedPly(E8, D8, Move, King),
                new RecordedPly(H3, H1, Move, Rook),
                new RecordedPly(D8, E8, Move, King)
            ],
            DoMove(E1, C1),
            Castling,
            new Board {
                [C1] = (White, King), [D1] = (White, Rook), [H1] = (White, Rook),
                [E8] = (Black, King)
            },
            Ongoing
        ),

        // ── Black Kingside Castling ──────────────────────────────────

        // Valid
        Custom(
            new Board {
                [E1] = (White, King),
                [E8] = (Black, King), [H8] = (Black, Rook)
            },
            Black, [],
            DoMove(E8, G8),
            Castling,
            new Board {
                [E1] = (White, King),
                [G8] = (Black, King), [F8] = (Black, Rook)
            },
            Ongoing
        ),

        // Blocked: piece on F8
        Custom(
            new Board {
                [E1] = (White, King),
                [E8] = (Black, King), [F8] = (Black, Bishop), [H8] = (Black, Rook)
            },
            Black, [],
            DoMove(E8, G8),
            Impossible,
            default,
            Ongoing
        ),

        // Attacked: opponent queen controls F8
        Custom(
            new Board {
                [E1] = (White, King), [F1] = (White, Queen),
                [E8] = (Black, King), [H8] = (Black, Rook)
            },
            Black, [],
            DoMove(E8, G8),
            Impossible,
            default,
            Ongoing
        ),

        // ── Black Queenside Castling ─────────────────────────────────

        // Valid
        Custom(
            new Board {
                [E1] = (White, King),
                [A8] = (Black, Rook), [E8] = (Black, King)
            },
            Black, [],
            DoMove(E8, C8),
            Castling,
            new Board {
                [E1] = (White, King),
                [C8] = (Black, King), [D8] = (Black, Rook)
            },
            Ongoing
        ),

        // B8 occupied: queenside castling still legal
        Custom(
            new Board {
                [E1] = (White, King),
                [A8] = (Black, Rook), [B8] = (Black, Knight), [E8] = (Black, King)
            },
            Black, [],
            DoMove(E8, C8),
            Castling,
            new Board {
                [E1] = (White, King),
                [B8] = (Black, Knight), [C8] = (Black, King), [D8] = (Black, Rook)
            },
            Ongoing
        ),

        // ── No rook present ─────────────────────────────────────────

        // Kingside: rook missing (captured), should be impossible
        Custom(
            new Board {
                [E1] = (White, King),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, G1),
            Impossible,
            default,
            Ongoing
        ),

        // Queenside: rook missing
        Custom(
            new Board {
                [E1] = (White, King),
                [E8] = (Black, King)
            },
            White, [],
            DoMove(E1, C1),
            Impossible,
            default,
            Ongoing
        ),
    ];

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
    => [new Game(board, side, plies), move, expectedResult, expectedBoard!, expectedStatus, captured, promoted];
}
