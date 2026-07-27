using Chess.Lib;
using Chess.UCI;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public sealed class UciTimeBudgetTests
{
    private static readonly TimeSpan Overhead = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void MoveTime_IsHonouredExactly_LessTheOverhead()
    {
        var budget = UciTimeBudget.ForMove(new UciCommand.Go(MoveTime: 1000), Side.White);

        budget.ShouldBe(TimeSpan.FromMilliseconds(1000) - Overhead);
    }

    [Fact]
    public void MoveTime_OutranksTheClock()
    {
        // "movetime" is an instruction, not a hint: the remaining clock must not dilute it.
        var go = new UciCommand.Go(MoveTime: 200, WTime: 600_000, BTime: 600_000);

        UciTimeBudget.ForMove(go, Side.White).ShouldBe(TimeSpan.FromMilliseconds(200) - Overhead);
    }

    [Fact]
    public void Infinite_IsUnbounded()
    {
        // The GUI is analysing and will send "stop"; depth is the only bound.
        UciTimeBudget.ForMove(new UciCommand.Go(Infinite: true, WTime: 60_000), Side.White).ShouldBeNull();
    }

    [Fact]
    public void NoClockAtAll_IsUnbounded()
    {
        UciTimeBudget.ForMove(new UciCommand.Go(), Side.White).ShouldBeNull();
        UciTimeBudget.ForMove(new UciCommand.Go(Depth: 6), Side.White).ShouldBeNull();
    }

    [Fact]
    public void EachSideGetsItsOwnClock()
    {
        var go = new UciCommand.Go(WTime: 300_000, BTime: 60_000);

        var white = UciTimeBudget.ForMove(go, Side.White)!.Value;
        var black = UciTimeBudget.ForMove(go, Side.Black)!.Value;

        // 300s/30 = 10s vs 60s/30 = 2s, both less the overhead.
        white.ShouldBe(TimeSpan.FromSeconds(10) - Overhead);
        black.ShouldBe(TimeSpan.FromSeconds(2) - Overhead);
    }

    [Fact]
    public void SuddenDeath_SpendsAThirtiethOfWhatIsLeft()
    {
        var budget = UciTimeBudget.ForMove(new UciCommand.Go(WTime: 60_000), Side.White)!.Value;

        budget.ShouldBe(TimeSpan.FromSeconds(2) - Overhead);
    }

    [Fact]
    public void MovesToGo_DividesTheRemainingPeriod()
    {
        var budget = UciTimeBudget.ForMove(new UciCommand.Go(WTime: 60_000, MovesToGo: 10), Side.White)!.Value;

        budget.ShouldBe(TimeSpan.FromSeconds(6) - Overhead);
    }

    [Fact]
    public void MovesToGoZero_MeansThisIsTheLastMoveOfThePeriod()
    {
        // Not a division by zero, and not the sudden-death default either: everything left is for
        // this move — subject to the "never commit more than 80%" clamp.
        var budget = UciTimeBudget.ForMove(new UciCommand.Go(WTime: 10_000, MovesToGo: 0), Side.White)!.Value;

        budget.ShouldBe(TimeSpan.FromSeconds(8) - Overhead);
    }

    [Fact]
    public void Increment_IsPartlyFoldedIn()
    {
        // 60s/30 = 2000ms, plus 75% of a 2s increment = 1500ms, less 50ms overhead.
        var go = new UciCommand.Go(WTime: 60_000, WInc: 2_000, BInc: 2_000);

        UciTimeBudget.ForMove(go, Side.White)!.Value.ShouldBe(TimeSpan.FromMilliseconds(3450));
    }

    [Fact]
    public void NeverCommitsMoreThanMostOfWhatIsLeft()
    {
        // A huge increment against a nearly-empty clock must not talk us into spending more than we
        // have. 1s/30 + 75% of 30s would be 22.5s; the clamp holds it to 80% of the 1s that is real.
        var go = new UciCommand.Go(WTime: 1_000, WInc: 30_000);

        UciTimeBudget.ForMove(go, Side.White)!.Value.ShouldBe(TimeSpan.FromMilliseconds(800) - Overhead);
    }

    [Fact]
    public void AlmostFlagged_StillReturnsSomethingPlayable()
    {
        // 30ms left: a share of that is under the overhead, so the subtraction would go negative.
        // The floor keeps it positive — the engine plays a depth-1 move rather than nothing.
        var budget = UciTimeBudget.ForMove(new UciCommand.Go(WTime: 30), Side.White)!.Value;

        budget.ShouldBeGreaterThan(TimeSpan.Zero);
        budget.ShouldBe(TimeSpan.FromMilliseconds(10));
    }
}
