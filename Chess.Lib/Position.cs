namespace Chess.Lib;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

public enum File : byte
{
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H
}

public enum Rank : byte
{
    R1,
    R2,
    R3,
    R4,
    R5,
    R6,
    R7,
    R8
}

public static class FileExtensions
{
    public static string ToLabel(this File file) => char.ToString((char)((byte)file + 'a'));
}

public static class RankExtensions
{
    public static string ToLabel(this Rank rank) => Convert.ToString((byte)rank + 1, CultureInfo.InvariantCulture);
}

public readonly record struct Position(File File, Rank Rank)
{
    public static readonly Position A1 = (File.A, Rank.R1);
    public static readonly Position A2 = (File.A, Rank.R2);
    public static readonly Position A3 = (File.A, Rank.R3);
    public static readonly Position A4 = (File.A, Rank.R4);
    public static readonly Position A5 = (File.A, Rank.R5);
    public static readonly Position A6 = (File.A, Rank.R6);
    public static readonly Position A7 = (File.A, Rank.R7);
    public static readonly Position A8 = (File.A, Rank.R8);
    public static readonly Position B1 = (File.B, Rank.R1);
    public static readonly Position B2 = (File.B, Rank.R2);
    public static readonly Position B3 = (File.B, Rank.R3);
    public static readonly Position B4 = (File.B, Rank.R4);
    public static readonly Position B5 = (File.B, Rank.R5);
    public static readonly Position B6 = (File.B, Rank.R6);
    public static readonly Position B7 = (File.B, Rank.R7);
    public static readonly Position B8 = (File.B, Rank.R8);
    public static readonly Position C1 = (File.C, Rank.R1);
    public static readonly Position C2 = (File.C, Rank.R2);
    public static readonly Position C3 = (File.C, Rank.R3);
    public static readonly Position C4 = (File.C, Rank.R4);
    public static readonly Position C5 = (File.C, Rank.R5);
    public static readonly Position C6 = (File.C, Rank.R6);
    public static readonly Position C7 = (File.C, Rank.R7);
    public static readonly Position C8 = (File.C, Rank.R8);
    public static readonly Position D1 = (File.D, Rank.R1);
    public static readonly Position D2 = (File.D, Rank.R2);
    public static readonly Position D3 = (File.D, Rank.R3);
    public static readonly Position D4 = (File.D, Rank.R4);
    public static readonly Position D5 = (File.D, Rank.R5);
    public static readonly Position D6 = (File.D, Rank.R6);
    public static readonly Position D7 = (File.D, Rank.R7);
    public static readonly Position D8 = (File.D, Rank.R8);
    public static readonly Position E1 = (File.E, Rank.R1);
    public static readonly Position E2 = (File.E, Rank.R2);
    public static readonly Position E3 = (File.E, Rank.R3);
    public static readonly Position E4 = (File.E, Rank.R4);
    public static readonly Position E5 = (File.E, Rank.R5);
    public static readonly Position E6 = (File.E, Rank.R6);
    public static readonly Position E7 = (File.E, Rank.R7);
    public static readonly Position E8 = (File.E, Rank.R8);
    public static readonly Position F1 = (File.F, Rank.R1);
    public static readonly Position F2 = (File.F, Rank.R2);
    public static readonly Position F3 = (File.F, Rank.R3);
    public static readonly Position F4 = (File.F, Rank.R4);
    public static readonly Position F5 = (File.F, Rank.R5);
    public static readonly Position F6 = (File.F, Rank.R6);
    public static readonly Position F7 = (File.F, Rank.R7);
    public static readonly Position F8 = (File.F, Rank.R8);
    public static readonly Position G1 = (File.G, Rank.R1);
    public static readonly Position G2 = (File.G, Rank.R2);
    public static readonly Position G3 = (File.G, Rank.R3);
    public static readonly Position G4 = (File.G, Rank.R4);
    public static readonly Position G5 = (File.G, Rank.R5);
    public static readonly Position G6 = (File.G, Rank.R6);
    public static readonly Position G7 = (File.G, Rank.R7);
    public static readonly Position G8 = (File.G, Rank.R8);
    public static readonly Position H1 = (File.H, Rank.R1);
    public static readonly Position H2 = (File.H, Rank.R2);
    public static readonly Position H3 = (File.H, Rank.R3);
    public static readonly Position H4 = (File.H, Rank.R4);
    public static readonly Position H5 = (File.H, Rank.R5);
    public static readonly Position H6 = (File.H, Rank.R6);
    public static readonly Position H7 = (File.H, Rank.R7);
    public static readonly Position H8 = (File.H, Rank.R8);

    public static readonly ImmutableArray<File> AllFiles = [File.A, File.B, File.C, File.D, File.E, File.F, File.G, File.H];
    public static readonly ImmutableArray<Rank> AllRanks = [Rank.R1, Rank.R2, Rank.R3, Rank.R4, Rank.R5, Rank.R6, Rank.R7, Rank.R8];

    private static readonly ImmutableArray<(int RankDelta, int FileDelta)> DiagonalPairs = [(-1, -1), (-1, 1), (1, -1), (1, 1)];

    public static IEnumerable<Position> AllPositions() => AllRanks
        .SelectMany(f => AllFiles, (r, f) => new Position(f, r));

    /// <summary>
    /// Returns all possible 
    /// </summary>
    /// <param name="current"></param>
    /// <param name="piece"></param>
    /// <returns></returns>
    public static IEnumerable<Position> AllPossibleActions(Position current, Piece piece)
    {
        return piece.PieceType switch
        {
            PieceType.King => AllKingPositions(current, piece.Side),
            PieceType.Queen => AllStraight(current).Concat(AllDiagonal(current)),
            PieceType.Rook => AllStraight(current),
            PieceType.Bishop => AllDiagonal(current),
            PieceType.Knight => AllKnightPositions(current),
            PieceType.Pawn => AllPawnPositions(current, piece.Side),
            _ => []
        };
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static IEnumerable<Position> AllStraight(Position position)
    {
        foreach (var file in AllFiles)
        {
            var possiblePos = new Position(file, position.Rank);
            if (possiblePos != position)
            {
                yield return possiblePos;
            }
        }

        foreach (var rank in AllRanks)
        {
            var possiblePos = new Position(position.File, rank);
            if (possiblePos != position)
            {
                yield return possiblePos;
            }
        }
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static IEnumerable<Position> AllDiagonal(Position position)
    {
        foreach (var (rankDelta, fileDelta) in DiagonalPairs)
        {
            var rank = (sbyte)position.Rank;
            var file = (sbyte)position.File;

            while (rank is >= 0 and < 8 && file is >= 0 and < 8)
            {
                var possiblePos = FromIndex(file, rank);
                if (possiblePos != position)
                {
                    yield return possiblePos;
                }

                rank = (sbyte)(rank + rankDelta);
                file = (sbyte)(file + fileDelta);
            }
        }
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static IEnumerable<Position> AllPawnPositions(Position position, Side side)
    {
        var newRank = (sbyte)((int)position.Rank + side.PawnDirection());
        if (newRank > 0 && newRank < 8)
        {
            var file = (sbyte)position.File;
            var fileLeft = (sbyte)(file - 1);
            var fileRight = (sbyte)(file + 1);

            yield return FromIndex(file, newRank);

            if (side.PawnRank() == position.Rank)
            {
                yield return FromIndex(file, (sbyte)(newRank + side.PawnDirection()));
            }

            if (fileLeft >= 0)
            {
                yield return FromIndex(fileLeft, newRank);
            }

            if (fileRight < 8)
            {
                yield return FromIndex(fileRight, newRank);
            }
        }
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static IEnumerable<Position> AllKnightPositions(Position position)
    {
        var fileValue = (int)position.File;
        var fileLeft1 = (sbyte)(fileValue - 1);
        var fileLeft2 = (sbyte)(fileValue - 2);
        var fileRight1 = (sbyte)(fileValue + 1);
        var fileRight2 = (sbyte)(fileValue + 2);

        var rankValue = (int)position.Rank;
        var rankDown1 = (sbyte)(rankValue - 1);
        var rankDown2 = (sbyte)(rankValue - 2);
        var rankUp1 = (sbyte)(rankValue + 1);
        var rankUp2 = (sbyte)(rankValue + 2);

        if (fileLeft1 >= 0 && rankDown2 >= 0)
        {
            yield return FromIndex(fileLeft1, rankDown2);
        }
        if (fileLeft1 >= 0 && rankUp2 < 8)
        {
            yield return FromIndex(fileLeft1, rankUp2);
        }

        if (fileLeft2 >= 0 && rankDown1 >= 0)
        {
            yield return FromIndex(fileLeft2, rankDown1);
        }
        if (fileLeft2 >= 0 && rankUp1 < 8)
        {
            yield return FromIndex(fileLeft2, rankUp1);
        }

        if (fileRight1 < 8 && rankDown2 >= 0)
        {
            yield return FromIndex(fileRight1, rankDown2);
        }
        if (fileRight1 < 8 && rankUp2 < 8)
        {
            yield return FromIndex(fileRight1, rankUp2);
        }

        if (fileRight2 < 8 && rankDown1 >= 0)
        {
            yield return FromIndex(fileRight2, rankDown1);
        }
        if (fileRight2 < 8 && rankUp1 < 8)
        {
            yield return FromIndex(fileRight2, rankUp1);
        }
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static IEnumerable<Position> AllKingPositions(Position position, Side side)
    {
        for (byte rank = (byte)Math.Max(0, (int)position.Rank - 1); rank < (byte)Math.Min(8, (int)position.Rank + 2); rank++)
        {
            for (byte file = (byte)Math.Max(0, (int)position.File - 1); file < (byte)Math.Min(8, (int)position.File + 2); file++)
            {
                var possiblePos = FromIndex(file, rank);

                if (possiblePos != position)
                {
                    yield return possiblePos;
                }
            }
        }

        var homeRank = side.HomeRank();
        if (position == new Position(File.E, homeRank))
        {
            yield return FromIndex((int)File.E - 2, (sbyte)homeRank);
            yield return FromIndex((int)File.E + 2, (sbyte)homeRank);
        }
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static implicit operator Position((File File, Rank Rank) Pair) => new Position(Pair.File, Pair.Rank);

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static Position FromIndex(sbyte file, sbyte rank)
    {
        if (file < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(file), file, "Must be 1..8");
        }

        if (rank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Must be 1..8");
        }

        return FromIndex((byte)file, (byte)rank);
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static Position FromIndex(byte file, byte rank)
    {
        if (file >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(file), file, "Must be 1..8");
        }

        if (rank >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Must be 1..8");
        }

        return new Position((File)file, (Rank)rank);
    }

    /// <summary>
    /// Returns a new position with the same file this position advanced by <paramref name="distance"/> ranks
    /// in the given <paramref name="side"/>'s pawn direction (<see cref="SideExtensions.PawnDirection(Side)"/>).
    /// </summary>
    /// <param name="position">Position to advance</param>
    /// <param name="side">Side the pawn belongs to</param>
    /// <param name="distance">Number of ranks to advance (1 or 2)</param>
    /// <returns></returns>
    public readonly Position AdvanceInPawnDirection(Side side, byte distance = 1)
    {
        if (distance is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "Can only advance by 1 or 2");
        }
        else if (distance == 2 && Rank != side.PawnRank())
        {
            throw new ArgumentException("Can only advance 2 ranks if pawn is on the pawn row", nameof(distance));
        }
        else if (side.ToOpposite().HomeRank() == Rank)
        {
            throw new ArgumentException("Pawn is already on the opposite home row, can't advance further", nameof(distance));
        }

        return FromIndex((byte)File, (byte)((int)Rank + side.PawnDirection() * distance));
    }

    public override string ToString() => File.ToLabel() + Rank.ToLabel();
}