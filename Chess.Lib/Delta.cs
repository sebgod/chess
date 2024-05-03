namespace Chess.Lib;

public readonly record struct Delta(sbyte File, sbyte Rank)
{
    public readonly bool IsStraight => (File != 0 && Rank == 0) || (File == 0 && Rank != 0);

    public readonly bool IsDiagnoal => File != 0 && Rank != 0 && AbsFile == AbsRank;

    public readonly byte AbsFile => (byte)Math.Abs(File);

    public readonly byte AbsRank => (byte)Math.Abs(Rank);

    public readonly bool IsLShape
    {
        get
        {
            var absFile = AbsFile;
            var absRank = AbsRank;

            return (absFile == 2 && absRank == 1) || (absFile == 1 && absRank == 2);
        }
    }
}
