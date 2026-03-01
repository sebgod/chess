using System.Collections.Immutable;
using Chess.Lib.UI;

namespace Chess.Console;

/// <summary>
/// Represents a player that can make moves through the game UI.
/// Returns <c>null</c> when idle (no input), or a result when input was processed.
/// </summary>
internal interface IGamePlayer
{
    (UIResponse Response, ImmutableArray<RectInt> ClipRects)? TryMakeMove(GameUI ui);
}
