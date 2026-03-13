using System.Collections.Concurrent;
using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using static SDL3.SDL;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.OpenGL;

public sealed class HumanPlayer : IGamePlayer
{
    private readonly ConcurrentQueue<InputEvent> _eventQueue = new();
    private File? _pendingFile;
    private int _lastKnownPlyCount = -1;

    private readonly record struct InputEvent(Scancode Scancode, bool IsCtrl, int MouseX, int MouseY, bool IsClick, bool IsScroll, int ScrollDelta);

    public void EnqueueKeyDown(Scancode scancode, Keymod keymod)
    {
        if (scancode == Scancode.F11) return;

        var isCtrl = (keymod & Keymod.Ctrl) != 0;
        _eventQueue.Enqueue(new InputEvent(scancode, isCtrl, 0, 0, false, false, 0));
    }

    public void EnqueueMouseDown(int x, int y)
    {
        _eventQueue.Enqueue(new InputEvent(Scancode.Unknown, false, x, y, true, false, 0));
    }

    public void EnqueueScroll(int delta)
    {
        _eventQueue.Enqueue(new InputEvent(Scancode.Unknown, false, 0, 0, false, true, delta));
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
            if (evt.IsClick || evt.Scancode is Scancode.F1 or Scancode.Escape)
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

        return HandleKeyInput(ui, evt.Scancode, evt.IsCtrl);
    }

    private PlayerMoveResult HandleKeyInput(GameUI ui, Scancode key, bool isCtrl)
    {
        if (key is Scancode.F1)
        {
            _pendingFile = null;
            return Result(ui.ToggleKeymap());
        }

        if (key is Scancode.F9)
        {
            _pendingFile = null;
            return Result(UIResponse.NeedsReset);
        }

        if (isCtrl)
        {
            if (key is Scancode.Left)  { _pendingFile = null; return Result(ui.NavigateBack()); }
            if (key is Scancode.Right) { _pendingFile = null; return Result(ui.NavigateForward()); }
            if (key is Scancode.Up)    { _pendingFile = null; return Result(ui.NavigateBack(2)); }
            if (key is Scancode.Down)  { _pendingFile = null; return Result(ui.NavigateForward(2)); }
        }

        if (key is Scancode.Pageup)
        {
            _pendingFile = null;
            return Result(ui.ScrollHistory(-(ui.HistoryViewportRows - 1)));
        }
        if (key is Scancode.Pagedown)
        {
            _pendingFile = null;
            return Result(ui.ScrollHistory(ui.HistoryViewportRows - 1));
        }

        if (ui.Mode == GameUIMode.Playback)
        {
            return key switch
            {
                Scancode.Escape => Result(ui.ExitPlayback()),
                Scancode.Left => Result(ui.NavigateBack()),
                Scancode.Right => Result(ui.NavigateForward()),
                Scancode.Up => Result(ui.NavigateBack(2)),
                Scancode.Down => Result(ui.NavigateForward(2)),
                _ => Result(UIResponse.None),
            };
        }

        if (ui.IsSetupMode)
            return HandleSetupKeyInput(ui, key);

        if (key is Scancode.Escape)
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
                Scancode.N => PieceType.Knight,
                Scancode.B => PieceType.Bishop,
                Scancode.R => PieceType.Rook,
                Scancode.Q => PieceType.Queen,
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

    private PlayerMoveResult HandleSetupKeyInput(GameUI ui, Scancode key)
    {
        if (key is Scancode.Tab)
        {
            _pendingFile = null;
            return Result(ui.TogglePlacementSide());
        }

        if (key is Scancode.S)
        {
            _pendingFile = null;
            ui.IsSetupMode = false;
            return Result(UIResponse.NeedsRefresh | UIResponse.IsUpdate);
        }

        if (ui.PendingPlacement is { } pendingPos)
        {
            if (key is Scancode.Escape)
            {
                _pendingFile = null;
                return Result(ui.CancelPlacement());
            }

            if (key is Scancode.Delete or Scancode.Backspace)
            {
                _pendingFile = null;
                return Result(ui.ClearSquare(pendingPos));
            }

            var pieceType = key switch
            {
                Scancode.P => PieceType.Pawn,
                Scancode.N => PieceType.Knight,
                Scancode.B => PieceType.Bishop,
                Scancode.R => PieceType.Rook,
                Scancode.Q => PieceType.Queen,
                Scancode.K => PieceType.King,
                _ => PieceType.None
            };

            if (pieceType is not PieceType.None)
            {
                _pendingFile = null;
                return Result(ui.TryPlacePiece(pendingPos, pieceType, ui.PlacementSide));
            }

            return Result(UIResponse.None);
        }

        if (key is Scancode.Escape)
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return Result(clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (key is Scancode.Backspace or Scancode.Delete)
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

    private static File? TryParseFile(Scancode key) => key switch
    {
        Scancode.A => File.A,
        Scancode.B => File.B,
        Scancode.C => File.C,
        Scancode.D => File.D,
        Scancode.E => File.E,
        Scancode.F => File.F,
        Scancode.G => File.G,
        Scancode.H => File.H,
        _ => null
    };

    private static Rank? TryParseRank(Scancode key) => key switch
    {
        Scancode.Alpha1 => Rank.R1,
        Scancode.Alpha2 => Rank.R2,
        Scancode.Alpha3 => Rank.R3,
        Scancode.Alpha4 => Rank.R4,
        Scancode.Alpha5 => Rank.R5,
        Scancode.Alpha6 => Rank.R6,
        Scancode.Alpha7 => Rank.R7,
        Scancode.Alpha8 => Rank.R8,
        _ => null
    };
}
