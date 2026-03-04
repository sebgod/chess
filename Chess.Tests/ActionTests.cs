using Chess.Lib;
using Shouldly;
using Xunit;
using static Chess.Lib.Position;

namespace Chess.Tests;

public class ActionTests
{
    [Theory]
    [MemberData(nameof(DataSource))]
    public void GivenAnActionWhenBetweenThenPositionsInBetweenFromAndToAreReturned(Lib.Action action, Position[] expectedInBetween)
    {
        action.Between().ToArray().ShouldBe(expectedInBetween);
    }

    public static IEnumerable<object[]> DataSource() => [
        InBetweenTest(D1, H5, [E2, F3, G4])
    ];

    private static object[] InBetweenTest(Position from, Position to, Position[] expectedPositions) => [Lib.Action.DoMove(from, to), expectedPositions];
}
