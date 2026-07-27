using Chess.Lib;

namespace Chess.UCI;

/// <summary>
/// Turns the clock half of a <see cref="UciCommand.Go"/> into a wall-clock budget for one move.
///
/// <para>UCI hands the engine the game state — how much each side has left, the increment, how many
/// moves remain in the period — and leaves the allocation policy entirely to the engine. This is that
/// policy, kept pure and separate from the search so it can be tested as a table.</para>
/// </summary>
public static class UciTimeBudget
{
    /// <summary>
    /// Moves assumed to remain when the GUI sends no <c>movestogo</c>, i.e. sudden death. Deliberately
    /// pessimistic: spending 1/30th of the clock each move decays rather than running out, and a game
    /// that ends sooner simply leaves time unspent.
    /// </summary>
    public const int DefaultMovesToGo = 30;

    /// <summary>
    /// Fraction of the increment folded into the budget. Not all of it, because the increment is only
    /// credited once the move is actually made — spending it in advance every move slowly overdraws.
    /// </summary>
    private const double IncrementUsage = 0.75;

    /// <summary>Never commit more than this share of what is actually left, whatever the arithmetic says.</summary>
    private const double MaxShareOfRemaining = 0.8;

    /// <summary>
    /// Held back to cover the round trip to the GUI and process scheduling. Losing on time because
    /// the move was still in a pipe is the one outcome worth being paranoid about.
    /// </summary>
    private static readonly TimeSpan MoveOverhead = TimeSpan.FromMilliseconds(50);

    /// <summary>Floor for a budget, so a nearly-flagged clock still returns something playable.</summary>
    private static readonly TimeSpan MinimumBudget = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Budget for the side about to move, or <c>null</c> for an unbounded search — which is what
    /// <c>go infinite</c>, a bare <c>go depth n</c> and a plain <c>go</c> all ask for. An unbounded
    /// search is bounded by depth instead, and by <c>stop</c>.
    /// </summary>
    public static TimeSpan? ForMove(UciCommand.Go go, Side sideToMove)
    {
        // An explicit movetime is an instruction, not a hint: search exactly that long. The overhead
        // still comes off — the GUI's deadline is measured at its end of the pipe, not ours.
        if (go.MoveTime is { } moveTime)
        {
            return Floor(TimeSpan.FromMilliseconds(moveTime) - MoveOverhead);
        }

        // "infinite" outranks the clock: the GUI is analysing and will send "stop" when it wants a move.
        if (go.Infinite)
        {
            return null;
        }

        var remainingMs = sideToMove == Side.White ? go.WTime : go.BTime;

        if (remainingMs is not { } remaining)
        {
            return null;
        }

        var incrementMs = (sideToMove == Side.White ? go.WInc : go.BInc) ?? 0;

        var movesToGo = go.MovesToGo switch
        {
            null => DefaultMovesToGo,
            // "0 moves to go" means this is the last move of the period — one move, not a division by zero.
            <= 0 => 1,
            { } m => m,
        };

        var share = (double)remaining / movesToGo + incrementMs * IncrementUsage;
        var capped = Math.Min(share, remaining * MaxShareOfRemaining);

        return Floor(TimeSpan.FromMilliseconds(capped) - MoveOverhead);
    }

    private static TimeSpan Floor(TimeSpan budget) => budget < MinimumBudget ? MinimumBudget : budget;
}
