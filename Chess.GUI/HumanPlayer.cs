using System.Collections.Concurrent;
using Chess.Lib.UI;
using DIR.Lib;

namespace Chess.GUI;

public sealed class HumanPlayer : IGamePlayer, IWidget
{
    private enum EventKind : byte { Key, Click, Scroll }

    private readonly ConcurrentQueue<InputEvent> _eventQueue = new();
    private int _lastKnownPlyCount = -1;

    private readonly record struct InputEvent(InputKey Key, InputModifier Modifiers, int X, int Y, EventKind Kind);

    public bool HandleKeyDown(InputKey key, InputModifier modifiers)
    {
        if (key == InputKey.F11) return false;

        _eventQueue.Enqueue(new InputEvent(key, modifiers, 0, 0, EventKind.Key));
        return true;
    }

    public bool HandleMouseDown(float x, float y)
    {
        _eventQueue.Enqueue(new InputEvent(InputKey.None, InputModifier.None, (int)x, (int)y, EventKind.Click));
        return true;
    }

    public bool HandleMouseWheel(float scrollY, float mouseX, float mouseY)
    {
        _eventQueue.Enqueue(new InputEvent(InputKey.None, InputModifier.None, 0, (int)scrollY, EventKind.Scroll));
        return true;
    }

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        var currentPlyCount = ui.Game.PlyCount;
        if (currentPlyCount != _lastKnownPlyCount)
        {
            _lastKnownPlyCount = currentPlyCount;
            _eventQueue.Clear();
            ui.PendingFile = null;
            return null;
        }

        if (!_eventQueue.TryDequeue(out var evt))
            return null;

        var (response, clips) = evt.Kind switch
        {
            EventKind.Key => ui.HandleKeyDown(evt.Key, evt.Modifiers),
            EventKind.Click => ui.HandleMouseDown(evt.X, evt.Y),
            EventKind.Scroll => ui.HandleMouseWheel(evt.Y),
            _ => (UIResponse.None, System.Collections.Immutable.ImmutableArray<RectInt>.Empty)
        };

        return new PlayerMoveResult(response, clips);
    }
}
