using System.Collections.Immutable;
using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>
/// Abstracts the display backend so the game loop is renderer-agnostic.
/// </summary>
public interface IGameDisplay : IDisposable
{
    GameUI UI { get; }
    void RenderInitial(Game game);
    void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects);
    void HandleResize(Game game);
    void ResetGame(Game game);
}
