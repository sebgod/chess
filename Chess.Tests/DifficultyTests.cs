using Chess.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public sealed class DifficultyTests
{
    [Theory]
    [InlineData(Difficulty.Easy, 2)]
    [InlineData(Difficulty.Normal, 3)]
    [InlineData(Difficulty.Hard, 4)]
    public void ToSearchDepth_MapsEachLevel(Difficulty difficulty, int expected) =>
        difficulty.ToSearchDepth().ShouldBe(expected);

    [Fact]
    public void Levels_GetStrongerMonotonically()
    {
        var depths = DifficultyExtensions.All.Select(d => d.ToSearchDepth()).ToArray();

        depths.ShouldBe(depths.Order().ToArray());
        depths.Distinct().Count().ShouldBe(depths.Length);
    }

    [Fact]
    public void EveryLevel_IsListedAndLabelled()
    {
        // All is what the wizard and Chess.Web's dropdown both render, so a level missing from it is
        // a level no front-end can reach.
        DifficultyExtensions.All.ShouldBe(Enum.GetValues<Difficulty>());
        DifficultyExtensions.All.Select(d => d.ToLabel()).ShouldBe(["Easy", "Normal", "Hard"]);
    }

    [Fact]
    public void TopLevel_StaysWithinWhatTheEngineCanAnswerPromptly()
    {
        // Depth 5 is ~1.4 s and depth 6 ~25 s on this engine (no transposition table), and Chess.Droid
        // searches on its UI thread. Raising this ceiling needs the engine to get faster first — see
        // DifficultyExtensions.ToSearchDepth.
        DifficultyExtensions.All.Max(d => d.ToSearchDepth()).ShouldBeLessThanOrEqualTo(4);
    }
}
