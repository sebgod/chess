using System.Collections.Immutable;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;

namespace Chess.Console;

/// <summary>
/// A human player that reads mouse and keyboard input from the terminal and translates them into game actions.
/// Uses <see cref="ConsoleInputMapping.ToInputEvent"/> to convert <see cref="ConsoleInputEvent"/> to the unified
/// <see cref="InputEvent"/> hierarchy, then dispatches to <see cref="GameUI.HandleKeyDown"/>,
/// <see cref="GameUI.HandleMouseDown"/>, <see cref="GameUI.HandlePointerUp"/>,
/// <see cref="GameUI.HandlePointerMove"/> and <see cref="GameUI.HandleMouseWheel"/>.
/// </summary>
internal sealed class HumanPlayer(IVirtualTerminal terminal) : IGamePlayer
{
    /// <summary>
    /// Damage from motion events whose render was skipped, unioned as it accumulates. See the
    /// coalescing note in <see cref="TryMakeMove"/>: the POSITION a coalesced event carried is stale,
    /// but the region it dirtied is not, and dropping it leaves the pixels a ghost vacated on screen
    /// until something unrelated happens to repaint them.
    /// </summary>
    private RectInt? _deferredDamage;

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
            return null;

        var consoleEvt = terminal.TryReadInput();
        var inputEvt = consoleEvt.ToInputEvent;

        // An event this mapping has no meaning for still has to flush deferred damage. Unmapped is the
        // COMMON case on a terminal — every escape sequence and unknown key lands here — so bypassing
        // the flush would routinely leave a drag's last frame unpainted until something else happened.
        if (inputEvt is null)
            return Result(Coalesce(inputEvt: null, UIResponse.None, []));

#if CONSOLE_INSPECTOR
        var selectedBefore = ui.Selected;
#endif

        var (response, clips) = inputEvt switch
        {
            InputEvent.Scroll s => ui.HandleMouseWheel((int)s.Delta),
            InputEvent.MouseDown m => ui.HandleMouseDown((int)m.X, (int)m.Y),
            // Terminals do report a release (ConsoleInputMapping maps SGR's IsRelease), so a
            // press-drag-release across the board completes a setup relocation here too.
            InputEvent.MouseUp m => ui.HandlePointerUp((int)m.X, (int)m.Y),
            // And they report the motion in between, at CELL resolution and only while a button is
            // held — \e[?1002h is button-motion tracking, not \e[?1003h any-event tracking, so there is
            // no idle hover stream to filter out. Motion goes through the normal player path rather
            // than round a queue, because this is the one display family that USES the clip rects it
            // is handed; on the GPU hosts they are discarded and a ghost costs a whole frame.
            InputEvent.MouseMove m => ui.HandlePointerMove((int)m.X, (int)m.Y),
            InputEvent.KeyDown k when k.Key != InputKey.None => ui.HandleKeyDown(k.Key, k.Modifiers),
            _ => (UIResponse.None, ImmutableArray<RectInt>.Empty)
        };

        (response, clips) = Coalesce(inputEvt, response, clips);

#if CONSOLE_INSPECTOR
        // The trace that identified the mouse-motion-as-click bug, now readable over the wire via the
        // inspector's `inputLog` instead of an env var and a redirected stderr file.
        InspectorHooks.Log(
            $"{inputEvt} -> {response} selected {selectedBefore?.ToString() ?? "-"}=>{ui.Selected?.ToString() ?? "-"} " +
            $"side={ui.Game.CurrentSide} plies={ui.Game.PlyCount} mode={ui.Mode}");
#endif

        return Result((response, clips));
    }

    /// <summary>
    /// Drops the render for a motion event that already has another event queued behind it, and
    /// carries its damage forward to whichever event does render.
    ///
    /// <para>Rendering every event of a fast drag is worse than rendering the latest one: each costs a
    /// partial sixel encode, and an intermediate position is stale before it reaches the screen. What
    /// is NOT stale is the region it dirtied — drop that and the pixels the ghost vacated stay on
    /// screen until something unrelated repaints them.</para>
    ///
    /// <para>Deferred damage is unioned rather than listed, which is both bounded (a long drag with a
    /// permanently non-empty queue cannot grow it) and lossless: <c>RenderFrame</c> unions every clip
    /// rect into one anyway.</para>
    /// </summary>
    private (UIResponse Response, ImmutableArray<RectInt> ClipRects) Coalesce(
        InputEvent? inputEvt, UIResponse response, ImmutableArray<RectInt> clips)
    {
        if (inputEvt is InputEvent.MouseMove && terminal.HasInput())
        {
            foreach (var rect in clips)
            {
                _deferredDamage = _deferredDamage is { } accumulated ? accumulated.Union(rect) : rect;
            }

            return (UIResponse.None, []);
        }

        if (_deferredDamage is not { } deferred)
        {
            return (response, clips);
        }

        _deferredDamage = null;

        // An EMPTY clip list means full frame, so a repaint that is already full covers everything
        // deferred; merging into it would wrongly narrow it to the ghost's rects.
        if (response.HasFlag(UIResponse.NeedsRefresh) && clips.IsDefaultOrEmpty)
        {
            return (response, clips);
        }

        // Otherwise the deferred region has to be painted by this event, including when this event
        // asked for nothing at all — an unmapped key would otherwise swallow the drag's last frame.
        return (response | UIResponse.NeedsRefresh,
            clips.IsDefaultOrEmpty ? [deferred] : clips.Add(deferred));
    }

    private static PlayerMoveResult Result((UIResponse Response, ImmutableArray<RectInt> ClipRects) uiResult)
        => new(uiResult.Response, uiResult.ClipRects);
}
