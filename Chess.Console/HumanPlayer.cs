using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// A human player that reads mouse and keyboard input from the terminal and translates them into game actions.
/// Keyboard input accepts a column letter (a-h) followed by a row digit (1-8) to select or move.
/// </summary>
internal sealed class HumanPlayer(ConsoleTerminal terminal) : IGamePlayer
{
    private File? _pendingFile;

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects)? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
        {
            return null;
        }

        var (mouseEvent, keyChar) = terminal.TryReadInput();
        if (mouseEvent is { Button: 0, IsRelease: false } mouse)
        {
            _pendingFile = null;
            return ui.TryPerformAction(mouse.X, mouse.Y);
        }

        if (keyChar is { } key)
        {
            return HandleKeyInput(ui, key);
        }

        return (UIResponse.None, []);
    }

    private (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleKeyInput(GameUI ui, char key)
    {
        if (TryParseFile(key) is { } file)
        {
            _pendingFile = file;
            return (UIResponse.None, []);
        }

        if (_pendingFile is { } pendingFile && TryParseRank(key) is { } rank)
        {
            _pendingFile = null;
            return ui.TryPerformAction(new Position(pendingFile, rank));
        }

        _pendingFile = null;
        return (UIResponse.None, []);
    }

    private static File? TryParseFile(char key) => char.ToLowerInvariant(key) switch
    {
        >= 'a' and <= 'h' => (File)(key - 'a'),
        _ => null
    };

    private static Rank? TryParseRank(char key) => key switch
    {
        >= '1' and <= '8' => (Rank)(key - '1'),
        _ => null
    };
}
