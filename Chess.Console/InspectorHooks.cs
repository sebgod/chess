#if CONSOLE_INSPECTOR
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib.Diagnostics;

namespace Chess.Console;

/// <summary>
/// The three places the debug inspector has to reach into a running game: the per-pump tick that lets its
/// queued commands run, the input trace, and the state snapshot.
///
/// <para>Static, which is not how production state is held here — <c>GameSession</c> owns that and every
/// front-end hosts it. But the display is rebuilt per game by a factory inside the loop, and the input
/// dispatch is three constructors away from where the inspector is created, so threading a reference through
/// both would put DEBUG-only plumbing into production signatures. The whole file is <c>#if DEBUG</c>, so a
/// release build has neither the state nor the calls.</para>
/// </summary>
internal static class InspectorHooks
{
    public static ConsoleDebugInspector? Instance { get; set; }

    /// <summary>The live display, registered by <c>ConsoleGameDisplayBase</c> as it is constructed.</summary>
    public static IGameDisplay? Display { get; set; }

    /// <summary>
    /// Runs queued inspector commands. Called from the display's per-pump hook, which is the loop thread —
    /// the same thread that mutates <see cref="GameUI"/>, so a command reads consistent state without a lock.
    /// </summary>
    public static void Pump() => Instance?.Pump();

    /// <summary>Records one input event and what it changed. See <see cref="ConsoleDebugInspector.LogInput"/>.</summary>
    public static void Log(string description) => Instance?.LogInput(description);

    /// <summary>
    /// The snapshot that would have turned this week's "the piece selects itself" report into one request
    /// instead of a manual session: what is selected, whose move it is, how many plies have been played, and
    /// which mode the UI is in.
    /// </summary>
    public static string AppState()
    {
        if (Display is not ConsoleGameDisplayBase<DIR.Lib.RgbaImage> display)
        {
            return "{\"error\":\"no display yet\"}";
        }

        GameUI ui;
        try
        {
            ui = display.UI;
        }
        catch (InvalidOperationException)
        {
            // UI throws until ResetGame has run; the menu and the wizard are both before that.
            return "{\"error\":\"no game yet\"}";
        }

        // Hand-built, not serialized from an anonymous type: chess is AOT-configured, so reflection-based
        // serialization is disabled and the generic overload throws at runtime. DebugInspectorCore.Quote
        // exists for exactly this.
        string Q(string? v) => DebugInspectorCore.Quote(v);

        // The render timing split rides along so a measurement is scriptable instead of parsed out of
        // the status bar's text. Invariant culture: a comma decimal separator would break the JSON.
        var renderStats = "";
        if (display.Stats is { } s)
        {
            renderStats = FormattableString.Invariant(
                $",\"paintMs\":{s.PaintMs:F1},\"sixelMs\":{s.SixelMs:F1},\"flushMs\":{s.FlushMs:F1},\"fullRenders\":{s.FullRenders},\"partialRenders\":{s.PartialRenders}");
        }

        return "{" +
            $"\"selected\":{Q(ui.Selected?.ToString())}," +
            $"\"pendingFile\":{Q(ui.PendingFile?.ToString())}," +
            $"\"sideToMove\":{Q(ui.Game.CurrentSide.ToString())}," +
            $"\"plies\":{ui.Game.PlyCount}," +
            $"\"mode\":{Q(ui.Mode.ToString())}," +
            $"\"status\":{Q(ui.StatusLine())}," +
            $"\"flipBoard\":{(ui.FlipBoard ? "true" : "false")}," +
            $"\"squareSize\":{ui.SquareSize}," +
            $"\"playbackPly\":{(ui.Mode == GameUIMode.Playback ? ui.PlaybackPlyIndex : -1)}" +
            renderStats +
            "}";
    }
}
#endif
