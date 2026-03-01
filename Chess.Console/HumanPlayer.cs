using System.Collections.Immutable;
using Chess.Lib.UI;

namespace Chess.Console;

/// <summary>
/// A human player that reads mouse input from the terminal and translates clicks into game actions.
/// </summary>
internal sealed class HumanPlayer(ConsoleTerminal terminal) : IGamePlayer
{
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects)? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
        {
            return null;
        }

        var mouseEvent = terminal.TryReadMouseEvent();
        if (mouseEvent is not { Button: 0, IsRelease: false })
        {
            return (UIResponse.None, []);
        }

        return ui.TryPerformAction(mouseEvent.Value.X, mouseEvent.Value.Y);
    }
}
