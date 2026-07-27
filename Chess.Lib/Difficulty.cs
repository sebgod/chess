namespace Chess.Lib;

/// <summary>
/// How hard the engine plays. A named level rather than a raw depth, so front-ends stop each
/// inventing their own scale — Chess.Web offered a 2/3/4 dropdown, Chess.Droid hard-coded 3, and the
/// desktop hosts silently took whatever <see cref="AiEngine.DefaultDepth"/> happened to be.
/// </summary>
public enum Difficulty
{
    Easy,
    Normal,
    Hard,
}

public static class DifficultyExtensions
{
    /// <summary>
    /// The search depth a level asks for.
    ///
    /// <para>The ladder stops at 4 because that is where this engine stops being playable, not out of
    /// caution. Measured warm on win-arm64 from the opening position: depth 2 ≈ 3 ms, depth 3 ≈ 14 ms,
    /// depth 4 ≈ 170 ms, depth 5 ≈ 1.4 s, <b>depth 6 ≈ 25 s</b>. Without a transposition table the
    /// branching swamps everything past 4, and Chess.Droid runs the search on its UI thread, where a
    /// second-long move already reads as a freeze. A stronger top level wants a transposition table
    /// first, not a bigger number here.</para>
    /// </summary>
    public static int ToSearchDepth(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 2,
        Difficulty.Normal => 3,
        Difficulty.Hard => 4,
        _ => AiEngine.DefaultDepth,
    };

    /// <summary>The level's menu label, so the wizard and every front-end spell it the same way.</summary>
    public static string ToLabel(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => "Easy",
        Difficulty.Normal => "Normal",
        Difficulty.Hard => "Hard",
        _ => difficulty.ToString(),
    };

    /// <summary>The levels in menu order — the single list every front-end presents.</summary>
    public static readonly Difficulty[] All = [Difficulty.Easy, Difficulty.Normal, Difficulty.Hard];
}
