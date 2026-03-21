using System.Collections.Concurrent;
using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.GUI;

public sealed class HumanPlayer : IGamePlayer
{
    private readonly ConcurrentQueue<InputEvent> _eventQueue = new();
    private File? _pendingFile;
    private int _lastKnownPlyCount = -1;

    private readonly record struct InputEvent(InputKey Key, bool IsCtrl, int MouseX, int MouseY, bool IsClick, bool IsScroll, int ScrollDelta);

    public void EnqueueKeyDown(InputKey key, InputModifier modifiers)
    {
        if (key == InputKey.F11) return;

        var isCtrl = (modifiers & InputModifier.Ctrl) != 0;
        _eventQueue.Enqueue(new InputEvent(key, isCtrl, 0, 0, false, false, 0));
    }

    public void EnqueueMouseDown(int x, int y)
    {
        _eventQueue.Enqueue(new InputEvent(InputKey.None, false, x, y, true, false, 0));
    }

    public void EnqueueScroll(int delta)
    {
        _eventQueue.Enqueue(new InputEvent(InputKey.None, false, 0, 0, false, true, delta));
    }

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        var currentPlyCount = ui.Game.PlyCount;
        if (currentPlyCount != _lastKnownPlyCount)
        {
            _lastKnownPlyCount = currentPlyCount;
            _eventQueue.Clear();
            _pendingFile = null;
            return null;
        }

        if (!_eventQueue.TryDequeue(out var evt))
            return null;

        if (ui.ShowingKeymap)
        {
            if (evt.IsClick || evt.Key is InputKey.F1 or InputKey.Escape)
            {
                _pendingFile = null;
                return Result(ui.ToggleKeymap());
            }
            return Result(UIResponse.None);
        }

        if (evt.IsScroll)
            return Result(ui.ScrollHistory(evt.ScrollDelta > 0 ? -3 : 3));

        if (evt.IsClick)
        {
            var hadPendingFile = _pendingFile is not null;
            _pendingFile = null;
            var (response, clips) = ui.TryPerformAction(evt.MouseX, evt.MouseY);
            if (hadPendingFile) response |= UIResponse.IsUpdate;
            return Result(response, clips);
        }

        return HandleKeyInput(ui, evt.Key, evt.IsCtrl);
    }

    private PlayerMoveResult HandleKeyInput(GameUI ui, InputKey key, bool isCtrl)
    {
        if (key is InputKey.F1)
        {
            _pendingFile = null;
            return Result(ui.ToggleKeymap());
        }

        if (key is InputKey.F9)
        {
            _pendingFile = null;
            return Result(UIResponse.NeedsReset);
        }

        if (isCtrl)
        {
            if (key is InputKey.Left)  { _pendingFile = null; return Result(ui.NavigateBack()); }
            if (key is InputKey.Right) { _pendingFile = null; return Result(ui.NavigateForward()); }
            if (key is InputKey.Up)    { _pendingFile = null; return Result(ui.NavigateBack(2)); }
            if (key is InputKey.Down)  { _pendingFile = null; return Result(ui.NavigateForward(2)); }
        }

        if (key is InputKey.PageUp)
        {
            _pendingFile = null;
            return Result(ui.ScrollHistory(-(ui.HistoryViewportRows - 1)));
        }
        if (key is InputKey.PageDown)
        {
            _pendingFile = null;
            return Result(ui.ScrollHistory(ui.HistoryViewportRows - 1));
        }

        if (ui.Mode == GameUIMode.Playback)
        {
            return key switch
            {
                InputKey.Escape => Result(ui.ExitPlayback()),
                InputKey.Left => Result(ui.NavigateBack()),
                InputKey.Right => Result(ui.NavigateForward()),
                InputKey.Up => Result(ui.NavigateBack(2)),
                InputKey.Down => Result(ui.NavigateForward(2)),
                _ => Result(UIResponse.None),
            };
        }

        if (ui.IsSetupMode)
            return HandleSetupKeyInput(ui, key);

        if (key is InputKey.Escape)
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

            if (ui.Selected is { } selected)
                return Result(ui.TryPerformAction(new Position(selected.File, rank)));
        }

        if (ui.PendingPromotion is { } pendingPromotion && ui.Selected is { } prev)
        {
            var promoteType = key switch
            {
                InputKey.N => PieceType.Knight,
                InputKey.B => PieceType.Bishop,
                InputKey.R => PieceType.Rook,
                InputKey.Q => PieceType.Queen,
                _ => PieceType.None
            };

            if (promoteType is not PieceType.None)
            {
                _pendingFile = null;
                return Result(ui.TryPerformAction(Action.Promote(prev, pendingPromotion, promoteType)));
            }
        }

        _pendingFile = null;
        return Result(UIResponse.None);
    }

    private PlayerMoveResult HandleSetupKeyInput(GameUI ui, InputKey key)
    {
        if (key is InputKey.Tab)
        {
            _pendingFile = null;
            return Result(ui.TogglePlacementSide());
        }

        if (key is InputKey.S)
        {
            _pendingFile = null;
            ui.IsSetupMode = false;
            return Result(UIResponse.NeedsRefresh | UIResponse.IsUpdate);
        }

        if (ui.PendingPlacement is { } pendingPos)
        {
            if (key is InputKey.Escape)
            {
                _pendingFile = null;
                return Result(ui.CancelPlacement());
            }

            if (key is InputKey.Delete or InputKey.Backspace)
            {
                _pendingFile = null;
                return Result(ui.ClearSquare(pendingPos));
            }

            var pieceType = key switch
            {
                InputKey.P => PieceType.Pawn,
                InputKey.N => PieceType.Knight,
                InputKey.B => PieceType.Bishop,
                InputKey.R => PieceType.Rook,
                InputKey.Q => PieceType.Queen,
                InputKey.K => PieceType.King,
                _ => PieceType.None
            };

            if (pieceType is not PieceType.None)
            {
                _pendingFile = null;
                return Result(ui.TryPlacePiece(pendingPos, pieceType, ui.PlacementSide));
            }

            return Result(UIResponse.None);
        }

        if (key is InputKey.Escape)
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return Result(clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (key is InputKey.Backspace or InputKey.Delete)
        {
            if (ui.Selected is { } selected)
            {
                _pendingFile = null;
                return Result(ui.ClearSquare(selected));
            }
        }

        if (TryParseFile(key) is { } file)
        {
            _pendingFile = file;
            return Result(UIResponse.IsUpdate);
        }

        if (TryParseRank(key) is { } rank && _pendingFile is { } pf)
        {
            _pendingFile = null;
            return Result(ui.SetupSelect(new Position(pf, rank)));
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

    private static File? TryParseFile(InputKey key) => key switch
    {
        InputKey.A => File.A,
        InputKey.B => File.B,
        InputKey.C => File.C,
        InputKey.D => File.D,
        InputKey.E => File.E,
        InputKey.F => File.F,
        InputKey.G => File.G,
        InputKey.H => File.H,
        _ => null
    };

    private static Rank? TryParseRank(InputKey key) => key switch
    {
        InputKey.D1 => Rank.R1,
        InputKey.D2 => Rank.R2,
        InputKey.D3 => Rank.R3,
        InputKey.D4 => Rank.R4,
        InputKey.D5 => Rank.R5,
        InputKey.D6 => Rank.R6,
        InputKey.D7 => Rank.R7,
        InputKey.D8 => Rank.R8,
        _ => null
    };
}
