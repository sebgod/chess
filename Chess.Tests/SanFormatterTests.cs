using System.Collections.Immutable;
using Chess.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.PieceType;
using static Chess.Lib.Position;
using static Chess.Lib.Side;

namespace Chess.Tests;

public class SanFormatterTests
{
    [Theory]
    [MemberData(nameof(DataSource))]
    public void ToSan_FormatsCorrectly(Board board, ImmutableList<RecordedPly> plies, Lib.Action action, string expected)
    {
        action.ToSan(board, plies).ShouldBe(expected);
    }

    public static IEnumerable<object[]> DataSource() => [
        // Quiet pawn move — pure target square
        Row(
            new Board { [E2] = (White, Pawn), [E1] = (White, King), [E8] = (Black, King) },
            DoMove(E2, E4),
            "e4"
        ),
        // Quiet piece move
        Row(
            new Board { [G1] = (White, Knight), [E1] = (White, King), [E8] = (Black, King) },
            DoMove(G1, F3),
            "Nf3"
        ),
        // Pawn capture: file letter + x + target
        Row(
            new Board {
                [E4] = (White, Pawn), [D5] = (Black, Pawn),
                [E1] = (White, King), [E8] = (Black, King)
            },
            DoMove(E4, D5),
            "exd5"
        ),
        // Piece capture
        Row(
            new Board {
                [F3] = (White, Bishop), [B7] = (Black, Pawn),
                [E1] = (White, King), [E8] = (Black, King)
            },
            DoMove(F3, B7),
            "Bxb7"
        ),
        // Promotion (no capture)
        Row(
            new Board { [A7] = (White, Pawn), [E1] = (White, King), [H8] = (Black, King) },
            Promote(A7, A8, Queen),
            "a8=Q+"
        ),
        // Promotion with capture
        Row(
            new Board {
                [A7] = (White, Pawn), [B8] = (Black, Rook),
                [E1] = (White, King), [H1] = (Black, King)
            },
            Promote(A7, B8, Queen),
            "axb8=Q"
        ),
        // Disambiguation by file: two rooks on the same rank can both reach d1
        Row(
            new Board {
                [A1] = (White, Rook), [H1] = (White, Rook),
                [E2] = (White, King), [E8] = (Black, King)
            },
            DoMove(A1, D1),
            "Rad1"
        ),
        // Disambiguation by rank: two rooks on the same file
        Row(
            new Board {
                [A1] = (White, Rook), [A5] = (White, Rook),
                [E2] = (White, King), [E8] = (Black, King)
            },
            DoMove(A1, A3),
            "R1a3"
        ),
        // Castling kingside
        Row(
            new Board {
                [E1] = (White, King), [H1] = (White, Rook), [E8] = (Black, King)
            },
            DoMove(E1, G1),
            "O-O"
        ),
        // Castling queenside
        Row(
            new Board {
                [E1] = (White, King), [A1] = (White, Rook), [E8] = (Black, King)
            },
            DoMove(E1, C1),
            "O-O-O"
        ),
        // Check suffix
        Row(
            new Board {
                [D1] = (White, Rook), [E1] = (White, King), [E8] = (Black, King)
            },
            DoMove(D1, D8),
            "Rd8+"
        ),
        // Mate suffix — back-rank mate (Bishop+Rook style: from puzzle 1)
        Row(
            new Board {
                [F7] = (White, Rook), [F3] = (White, Bishop), [D4] = (White, King),
                [E6] = (Black, King), [D6] = (Black, Pawn)
            },
            DoMove(F3, D5),
            "Bd5#"
        ),
    ];

    private static object[] Row(Board board, Lib.Action action, string expected)
        => [board, ImmutableList<RecordedPly>.Empty, action, expected];
}
