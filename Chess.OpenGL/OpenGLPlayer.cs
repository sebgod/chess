using System.Collections.Concurrent;
using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Silk.NET.Input;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;
using Key = Silk.NET.Input.Key;

namespace Chess.OpenGL;

/// <summary>
/// An <see cref="IGamePlayer"/> that translates Silk.NET keyboard and mouse events into chess game actions.
/// Events are enqueued from the window's input callbacks and dequeued by the game loop thread.
/// </summary>
public sealed class OpenGLPlayer : IGamePlayer
{
    private readonly ConcurrentQueue<InputEvent> _eventQueue = new();
    private File? _pendingFile;
    private int _lastKnownPlyCount = -1;

    private readonly record struct InputEvent(Key Key, bool IsCtrl, int MouseX, int MouseY, bool IsClick, bool IsScroll, int ScrollDelta);

    /// <summary>
    /// Connects this player to the given Silk.NET input context.
    /// Call after the window has loaded and input is available.
    /// </summary>
    public void Attach(IInputContext input)
    {
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.Scroll += OnMouseScroll;
        }
    }

    /// <inheritdoc />
    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        // If the game state changed while we weren't being polled (e.g. engine move),
        // discard any stale input that accumulated during the wait.
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

        if (evt.IsScroll)
        {
            return Result(ui.ScrollHistory(evt.ScrollDelta > 0 ? -3 : 3));
        }

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

    private PlayerMoveResult HandleKeyInput(GameUI ui, Key key, bool isCtrl)
    {
        if (ui.ShowingKeymap)
        {
            if (key is Key.F1 or Key.Escape)
            {
                _pendingFile = null;
                return Result(ui.ToggleKeymap());
            }
            return Result(UIResponse.None);
        }

        if (key is Key.F1)
        {
            _pendingFile = null;
            return Result(ui.ToggleKeymap());
        }

        if (key is Key.F9)
        {
            _pendingFile = null;
            return Result(UIResponse.NeedsReset);
        }

        // Playback navigation
        if (isCtrl)
        {
            if (key is Key.Left)  { _pendingFile = null; return Result(ui.NavigateBack()); }
            if (key is Key.Right) { _pendingFile = null; return Result(ui.NavigateForward()); }
            if (key is Key.Up)    { _pendingFile = null; return Result(ui.NavigateBack(2)); }
            if (key is Key.Down)  { _pendingFile = null; return Result(ui.NavigateForward(2)); }
        }

        if (key is Key.PageUp)
        {
            _pendingFile = null;
            return Result(ui.ScrollHistory(-(ui.HistoryViewportRows - 1)));
        }
        if (key is Key.PageDown)
        {
            _pendingFile = null;
            return Result(ui.ScrollHistory(ui.HistoryViewportRows - 1));
        }

        if (ui.Mode == GameUIMode.Playback)
        {
            return key switch
            {
                Key.Escape => Result(ui.ExitPlayback()),
                Key.Left => Result(ui.NavigateBack()),
                Key.Right => Result(ui.NavigateForward()),
                Key.Up => Result(ui.NavigateBack(2)),
                Key.Down => Result(ui.NavigateForward(2)),
                _ => Result(UIResponse.None),
            };
        }

        if (ui.IsSetupMode)
            return HandleSetupKeyInput(ui, key);

        if (key is Key.Escape)
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

        // Promotion keys
        if (ui.PendingPromotion is { } pendingPromotion && ui.Selected is { } prev)
        {
            var promoteType = key switch
            {
                Key.N => PieceType.Knight,
                Key.B => PieceType.Bishop,
                Key.R => PieceType.Rook,
                Key.Q => PieceType.Queen,
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

    private PlayerMoveResult HandleSetupKeyInput(GameUI ui, Key key)
    {
        if (key is Key.Tab)
        {
            _pendingFile = null;
            return Result(ui.TogglePlacementSide());
        }

        if (key is Key.S)
        {
            _pendingFile = null;
            ui.IsSetupMode = false;
            return Result(UIResponse.NeedsRefresh | UIResponse.IsUpdate);
        }

        if (ui.PendingPlacement is { } pendingPos)
        {
            if (key is Key.Escape)
            {
                _pendingFile = null;
                return Result(ui.CancelPlacement());
            }

            if (key is Key.Delete or Key.Backspace)
            {
                _pendingFile = null;
                return Result(ui.ClearSquare(pendingPos));
            }

            var pieceType = key switch
            {
                Key.P => PieceType.Pawn,
                Key.N => PieceType.Knight,
                Key.B => PieceType.Bishop,
                Key.R => PieceType.Rook,
                Key.Q => PieceType.Queen,
                Key.K => PieceType.King,
                _ => PieceType.None
            };

            if (pieceType is not PieceType.None)
            {
                _pendingFile = null;
                return Result(ui.TryPlacePiece(pendingPos, pieceType, ui.PlacementSide));
            }

            return Result(UIResponse.None);
        }

        if (key is Key.Escape)
        {
            _pendingFile = null;
            var (clearResponse, clearClips) = ui.ClearSelection();
            return Result(clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (key is Key.Backspace or Key.Delete)
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

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        var isCtrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        _eventQueue.Enqueue(new InputEvent(key, isCtrl, 0, 0, false, false, 0));
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _eventQueue.Enqueue(new InputEvent(Key.Unknown, false, (int)mouse.Position.X, (int)mouse.Position.Y, true, false, 0));
        }
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        _eventQueue.Enqueue(new InputEvent(Key.Unknown, false, 0, 0, false, true, (int)scroll.Y));
    }

    private PlayerMoveResult Result(UIResponse response)
        => new(response, [], _pendingFile);

    private PlayerMoveResult Result(UIResponse response, ImmutableArray<RectInt> clipRects)
        => new(response, clipRects, _pendingFile);

    private PlayerMoveResult Result((UIResponse Response, ImmutableArray<RectInt> ClipRects) uiResult)
        => new(uiResult.Response, uiResult.ClipRects, _pendingFile);

    private static File? TryParseFile(Key key) => key switch
    {
        Key.A => File.A,
        Key.B => File.B,
        Key.C => File.C,
        Key.D => File.D,
        Key.E => File.E,
        Key.F => File.F,
        Key.G => File.G,
        Key.H => File.H,
        _ => null
    };

    private static Rank? TryParseRank(Key key) => key switch
    {
        Key.Number1 => Rank.R1,
        Key.Number2 => Rank.R2,
        Key.Number3 => Rank.R3,
        Key.Number4 => Rank.R4,
        Key.Number5 => Rank.R5,
        Key.Number6 => Rank.R6,
        Key.Number7 => Rank.R7,
        Key.Number8 => Rank.R8,
        _ => null
    };
}
