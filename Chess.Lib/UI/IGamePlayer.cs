using System.Collections.Immutable;

namespace Chess.Lib.UI;

/// <summary>
/// The result of a player input: what changed, which screen regions to redraw, and the pending file (if any).
/// </summary>
public readonly record struct PlayerMoveResult(UIResponse Response, ImmutableArray<RectInt> ClipRects, File? PendingFile = null);

/// <summary>
/// Represents a player that can make moves through the game UI.
/// Returns <c>null</c> when idle (no input), or a result when input was processed.
/// </summary>
public interface IGamePlayer
{
    PlayerMoveResult? TryMakeMove(GameUI ui);
}
