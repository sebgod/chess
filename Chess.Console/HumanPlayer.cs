using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;

namespace Chess.Console;

/// <summary>
/// A human player that reads mouse and keyboard input from the terminal and translates them into game actions.
/// Uses <see cref="ConsoleInputMapping.ToInputEvent"/> to convert <see cref="ConsoleInputEvent"/> to the unified
/// <see cref="InputEvent"/> hierarchy, then dispatches to <see cref="GameUI.HandleKeyDown"/>,
/// <see cref="GameUI.HandleMouseDown"/>, and <see cref="GameUI.HandleMouseWheel"/>.
/// </summary>
internal sealed class HumanPlayer(IVirtualTerminal terminal) : IGamePlayer
{
    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
            return null;

        var consoleEvt = terminal.TryReadInput();
        var inputEvt = consoleEvt.ToInputEvent;

        if (inputEvt is null)
            return Result((UIResponse.None, []));

        var (response, clips) = inputEvt switch
        {
            InputEvent.Scroll s => ui.HandleMouseWheel((int)s.Delta),
            InputEvent.MouseDown m => ui.HandleMouseDown((int)m.X, (int)m.Y),
            InputEvent.KeyDown k when k.Key != InputKey.None => ui.HandleKeyDown(k.Key, k.Modifiers),
            _ => (UIResponse.None, System.Collections.Immutable.ImmutableArray<RectInt>.Empty)
        };

        return Result((response, clips));
    }

    private static PlayerMoveResult Result((UIResponse Response, System.Collections.Immutable.ImmutableArray<RectInt> ClipRects) uiResult)
        => new(uiResult.Response, uiResult.ClipRects);
}
