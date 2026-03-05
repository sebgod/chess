using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;

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
internal sealed class HumanPlayer(IVirtualTerminal terminal) : IGamePlayer
{
    private File? _pendingFile;

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        if (!terminal.HasInput())
        {
            return null;
        }

        var (mouseEvent, key, modifiers) = terminal.TryReadInput();
        if (mouseEvent is { Button: 0, IsRelease: true } mouse)
        {
            var hadPendingFile = _pendingFile is not null;
            _pendingFile = null;
            var (response, clips) = ui.TryPerformAction(mouse.X, mouse.Y);
            if (hadPendingFile) response |= UIResponse.IsUpdate;
            return Result(response, clips);
        }

        if (key is not ConsoleKey.None)
        {
            return HandleKeyInput(ui, key, modifiers);
        }

        return Result(UIResponse.None);
    }

    private PlayerMoveResult HandleKeyInput(GameUI ui, ConsoleKey key, ConsoleModifiers modifiers)
    {
        // Keymap overlay: '?' toggles, Escape dismisses
        if (ui.ShowingKeymap)
        {
            if (key is ConsoleKey.F1 or ConsoleKey.Escape)
            {
                _pendingFile = null;
                return Result(ui.ToggleKeymap());
            }
            return Result(UIResponse.None);
        }

        if (key is ConsoleKey.F1)
        {
            _pendingFile = null;
            return Result(ui.ToggleKeymap());
        }

        // Playback navigation: Ctrl+Arrow
        if (modifiers.HasFlag(ConsoleModifiers.Control))
        {
            if (key is ConsoleKey.LeftArrow)
            {
                _pendingFile = null;
                return Result(ui.NavigateBack());
            }
            if (key is ConsoleKey.RightArrow)
            {
                _pendingFile = null;
                return Result(ui.NavigateForward());
            }
            if (key is ConsoleKey.UpArrow)
            {
                _pendingFile = null;
                return Result(ui.NavigateBack(2));
            }
            if (key is ConsoleKey.DownArrow)
            {
                _pendingFile = null;
                return Result(ui.NavigateForward(2));
            }
        }

        // During playback, only Escape (to exit) is allowed
        if (ui.Mode == GameUIMode.Playback)
        {
            if (key is ConsoleKey.Escape)
            {
                _pendingFile = null;
                return Result(ui.ExitPlayback());
            }
            return Result(UIResponse.None);
        }

        if (ui.IsSetupMode)
        {
            return HandleSetupKeyInput(ui, key);
        }

        if (key is ConsoleKey.Escape)
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return Result(clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (TryParseFile(key) is { } file)
        {
            _pendingFile = file;
            return Result(UIResponse.IsUpdate);
        }

        if (TryParseRank(key) is { } rank)
        {
            if (_pendingFile is { } pendingFile)
            {
                _pendingFile = null;
                var (response, clips) = ui.TryPerformAction(new Position(pendingFile, rank));
                return Result(response | UIResponse.IsUpdate, clips);
            }

            // Rank-only shortcut: move selected piece along its file
            if (ui.Selected is { } selected)
            {
                return Result(ui.TryPerformAction(new Position(selected.File, rank)));
            }
        }

        _pendingFile = null;
        return Result(UIResponse.None);
    }

    private PlayerMoveResult HandleSetupKeyInput(GameUI ui, ConsoleKey key)
    {
        // Tab toggles placement side
        if (key is ConsoleKey.Tab)
        {
            _pendingFile = null;
            return Result(ui.TogglePlacementSide());
        }

        // 's' ends setup mode and starts the game
        if (key is ConsoleKey.S)
        {
            _pendingFile = null;
            ui.IsSetupMode = false;
            return Result(UIResponse.NeedsRefresh | UIResponse.IsUpdate);
        }

        // When piece popup is open
        if (ui.PendingPlacement is { } pendingPos)
        {
            // Escape cancels the popup
            if (key is ConsoleKey.Escape)
            {
                _pendingFile = null;
                return Result(ui.CancelPlacement());
            }

            // Delete/Backspace clears the square
            if (key is ConsoleKey.Delete or ConsoleKey.Backspace)
            {
                _pendingFile = null;
                return Result(ui.ClearSquare(pendingPos));
            }

            // Piece key shortcuts
            if (PieceType.TryParseFromKey(key) is { } pieceType)
            {
                _pendingFile = null;
                return Result(ui.TryPlacePiece(pendingPos, pieceType, ui.PlacementSide));
            }

            return Result(UIResponse.None);
        }

        // Escape clears selection
        if (key is ConsoleKey.Escape)
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return Result(clearResponse | UIResponse.IsUpdate, clearClips);
        }

        // Delete/Backspace clears piece at selected square
        if (key is ConsoleKey.Backspace or ConsoleKey.Delete)
        {
            if (ui.Selected is { } selected)
            {
                _pendingFile = null;
                return Result(ui.ClearSquare(selected));
            }
        }

        // File + rank to select a square for placement
        if (TryParseFile(key) is { } file)
        {
            _pendingFile = file;
            return Result(UIResponse.IsUpdate);
        }

        if (TryParseRank(key) is { } rank)
        {
            if (_pendingFile is { } pendingFile)
            {
                _pendingFile = null;
                return Result(ui.SetupSelect(new Position(pendingFile, rank)));
            }
        }

        _pendingFile = null;
        return Result(UIResponse.None);
    }

    private PlayerMoveResult Result(UIResponse response)
        => new(response, [], _pendingFile);

    private PlayerMoveResult Result(UIResponse response, ImmutableArray<RectInt> clipRects)
        => new(response, clipRects, _pendingFile);

    private PlayerMoveResult Result((UIResponse Response, ImmutableArray<RectInt> ClipRects) uiResult)
        => new(uiResult.Response, uiResult.ClipRects, _pendingFile);

    private static File? TryParseFile(ConsoleKey key)
    {
        return key is >= ConsoleKey.A and <= ConsoleKey.H ? (File)(key - ConsoleKey.A) : null;
    }

    private static Rank? TryParseRank(ConsoleKey key) => key switch
    {
        >= ConsoleKey.D1 and <= ConsoleKey.D8 => (Rank)(key - ConsoleKey.D1),
        _ => null
    };
}
