using Chess.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.PieceType;
using static Chess.Lib.Position;
using static Chess.Lib.Side;

namespace Chess.Tests;

public class PositionTests
{
    [Theory]
    [MemberData(nameof(DataSource))]
    public void TestWhenGivenPositionWhenAllPossibleActionsThenTheyAreReturned(PieceType pieceType, Position position, Side side, Position[] expectedPositions)
    {
        AllPossibleActions(position, new Piece(pieceType, side)).ToArray().ShouldBe(expectedPositions, ignoreOrder: true);
    }

    public static IEnumerable<object[]> DataSource() => [
        PositionTest(Pawn,   A2, White, [A3, A4, B3]),
        PositionTest(Pawn,   E7, Black, [E6, E5, D6, F6]),
        PositionTest(Pawn,   C4, White, [C5, B5, D5]),
        PositionTest(Pawn,   G8, White, []),
        PositionTest(Pawn,   A1, Black, []),
        PositionTest(Knight, B1, White, [A3, C3, D2]),
        PositionTest(Knight, D4, Black, [F3, F5, B3, B5, C6, C2, E6, E2]),
        PositionTest(Bishop, E5, Black, [D4, C3, B2, A1, F6, G7, H8, F4, G3, H2, D6, C7, B8]),
        PositionTest(Bishop, A8, White, [B7, C6, D5, E4, F3, G2, H1]),
        PositionTest(Rook,   H8, Black, [G8, F8, E8, D8, C8, B8, A8, H7, H6, H5, H4, H3, H2, H1]),
        PositionTest(Queen,  D1, White, [A1, B1, C1, E1, F1, G1, H1, C2, B3, A4, E2, F3, G4, H5, D2, D3, D4, D5, D6, D7, D8]),
        PositionTest(Queen,  D8, Black, [A8, B8, C8, E8, F8, G8, H8, C7, B6, A5, E7, F6, G5, H4, D7, D6, D5, D4, D3, D2, D1]),
        PositionTest(King,   E5, White, [D4, E4, F4, D5, F5, D6, E6, F6]),
        PositionTest(King,   E1, White, [D1, F1, D2, E2, F2, C1, G1]),
        PositionTest(King,   E1, Black, [D1, F1, D2, E2, F2]),
        PositionTest(King,   E8, White, [D8, F8, D7, E7, F7]),
        PositionTest(King,   E8, Black, [D8, F8, D7, E7, F7, C8, G8])
    ];

    private static object[] PositionTest(PieceType piece, Position position, Side side, Position[] expectedPositions) => [piece, position, side, expectedPositions];
}
