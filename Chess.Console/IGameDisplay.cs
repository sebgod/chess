using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Abstracts the display backend so the game loop is renderer-agnostic.
/// </summary>
internal interface IGameDisplay : IDisposable
{
    GameUI UI { get; }
    void RenderInitial(Game game, File? pendingFile = null);
    void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile = null);
    void HandleResize(Game game);
}
