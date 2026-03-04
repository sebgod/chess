using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// A human player that reads mouse and keyboard input from the terminal and translates them into game actions.
/// Keyboard input supports three modes:
/// <list type="bullet">
///   <item>Column letter (a-h) + row digit (1-8) to select a square or specify a move target.</item>
///   <item>When a piece is already selected, column + row to move it to a different square.</item>
///   <item>When a piece is already selected, just a row digit to move along the same file.</item>
/// </list>
/// </summary>
internal sealed class HumanPlayer(ConsoleTerminal terminal) : IGamePlayer
{
    private File? _pendingFile;

    public File? PendingFile => _pendingFile;

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects)? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
        {
            return null;
        }

        var (mouseEvent, keyChar) = terminal.TryReadInput();
        if (mouseEvent is { Button: 0, IsRelease: true } mouse)
        {
            var hadPendingFile = _pendingFile is not null;
            _pendingFile = null;
            var (response, clips) = ui.TryPerformAction(mouse.X, mouse.Y);
            if (hadPendingFile) response |= UIResponse.IsUpdate;
            return (response, clips);
        }

        if (keyChar is { } key)
        {
            return HandleKeyInput(ui, key);
        }

        return (UIResponse.None, []);
    }

    private (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleKeyInput(GameUI ui, char key)
    {
        if (ui.IsSetupMode)
        {
            return HandleSetupKeyInput(ui, key);
        }

        if (key is '\x1B') // Escape
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return (clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (TryParseFile(key) is { } file)
        {
            _pendingFile = file;
            return (UIResponse.IsUpdate, []);
        }

        if (TryParseRank(key) is { } rank)
        {
            if (_pendingFile is { } pendingFile)
            {
                _pendingFile = null;
                var (response, clips) = ui.TryPerformAction(new Position(pendingFile, rank));
                return (response | UIResponse.IsUpdate, clips);
            }

            // Rank-only shortcut: move selected piece along its file
            if (ui.Selected is { } selected)
            {
                return ui.TryPerformAction(new Position(selected.File, rank));
            }
        }

        _pendingFile = null;
        return (UIResponse.None, []);
    }

    private (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleSetupKeyInput(GameUI ui, char key)
    {
        // Tab toggles placement side
        if (key is '\t')
        {
            _pendingFile = null;
            return ui.TogglePlacementSide();
        }

        // 's' ends setup mode and starts the game
        if (key is 's' or 'S')
        {
            _pendingFile = null;
            ui.IsSetupMode = false;
            return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
        }

        // When piece popup is open
        if (ui.PendingPlacement is { } pendingPos)
        {
            // Escape cancels the popup
            if (key is '\x1B')
            {
                _pendingFile = null;
                return ui.CancelPlacement();
            }

            // Delete/Backspace clears the square
            if (key is '\x7F' or '\b')
            {
                _pendingFile = null;
                return ui.ClearSquare(pendingPos);
            }

            // Piece key shortcuts
            if (PieceType.TryParseFromKey(key) is { } pieceType)
            {
                _pendingFile = null;
                return ui.TryPlacePiece(pendingPos, pieceType, ui.PlacementSide);
            }

            return (UIResponse.None, []);
        }

        // Escape clears selection
        if (key is '\x1B')
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return (clearResponse | UIResponse.IsUpdate, clearClips);
        }

        // Delete/Backspace clears piece at selected square
        if (key is '\x7F' or '\b')
        {
            if (ui.Selected is { } selected)
            {
                _pendingFile = null;
                return ui.ClearSquare(selected);
            }
        }

        // File + rank to select a square for placement
        if (TryParseFile(key) is { } file)
        {
            _pendingFile = file;
            return (UIResponse.IsUpdate, []);
        }

        if (TryParseRank(key) is { } rank)
        {
            if (_pendingFile is { } pendingFile)
            {
                _pendingFile = null;
                return ui.SetupSelect(new Position(pendingFile, rank));
            }
        }

        _pendingFile = null;
        return (UIResponse.None, []);
    }


    private static File? TryParseFile(char key)
    {
        var lower = char.ToLowerInvariant(key);
        return lower is >= 'a' and <= 'h' ? (File)(lower - 'a') : null;
    }

    private static Rank? TryParseRank(char key) => key switch
    {
        >= '1' and <= '8' => (Rank)(key - '1'),
        _ => null
    };
}
