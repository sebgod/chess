using Chess.Lib;
using NUnit.Framework;
using Shouldly;
using static Chess.Lib.Position;

namespace Chess.Tests;

public class ActionTests
{
    [Test]
    [TestCaseSource(nameof(DataSource))]
    public void GivenAnActionWhenBetweenThenPositionsInBetweenFromAndToAreReturned(Lib.Action action, Position[] expectedInBetween)
    {
        action.Between().ToArray().ShouldBe(expectedInBetween);
    }

    public static IEnumerable<TestCaseData> DataSource() => [
        InBetweenTest(D1, H5, [E2, F3, G4])
    ];

    private static TestCaseData InBetweenTest(Position from, Position to, Position[] expectedPositions) => new TestCaseData(Lib.Action.DoMove(from, to), expectedPositions);
}