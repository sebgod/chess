using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;

namespace Chess.Console;

/// <summary>
/// An AI player that wraps <see cref="AiEngine"/> to make moves through the game UI.
/// </summary>
internal sealed class AiPlayer(AiEngine engine) : IGamePlayer
{
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects)? TryMakeMove(GameUI ui)
    {
        if (engine.PickMove(ui.Game) is { } action)
        {
            return ui.TryPerformAction(action);
        }

        return null;
    }
}
