using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;

namespace Chess.Console;

/// <summary>
/// A human player that reads mouse and keyboard input from the terminal and translates them into game actions.
/// Uses <see cref="ConsoleInputMapping"/> to convert <see cref="ConsoleKey"/> to <see cref="InputKey"/>,
/// then delegates to <see cref="GameUI.HandleKeyDown"/>, <see cref="GameUI.HandleMouseDown"/>,
/// and <see cref="GameUI.HandleMouseWheel"/> for unified input handling.
/// </summary>
internal sealed class HumanPlayer(IVirtualTerminal terminal) : IGamePlayer
{
    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
            return null;

        var evt = terminal.TryReadInput();

        if (evt.Mouse is { Button: 64 or 65 } wheel)
            return Result(ui.HandleMouseWheel(wheel.Button == 64 ? -1 : 1));

        if (evt.Mouse is { Button: 0, IsRelease: true } mouse)
            return Result(ui.HandleMouseDown(mouse.X, mouse.Y));

        if (evt.Key is not ConsoleKey.None)
        {
            var inputKey = evt.Key.ToInputKey;
            var inputMod = evt.Modifiers.ToInputModifier;
            if (inputKey != InputKey.None)
                return Result(ui.HandleKeyDown(inputKey, inputMod));
        }

        return Result((UIResponse.None, []));
    }

    private static PlayerMoveResult Result((UIResponse Response, System.Collections.Immutable.ImmutableArray<RectInt> ClipRects) uiResult)
        => new(uiResult.Response, uiResult.ClipRects);
}
