using System.Collections.Concurrent;
using Chess.Lib.UI;
using DIR.Lib;

namespace Chess.GUI;

public sealed class HumanPlayer : IGamePlayer, IWidget
{
    private readonly ConcurrentQueue<InputEvent> _eventQueue = new();
    private int _lastKnownPlyCount = -1;

    public bool HandleInput(InputEvent evt)
    {
        if (evt is InputEvent.KeyDown(InputKey.F11, _)) return false;

        _eventQueue.Enqueue(evt);
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

        var (response, clips) = evt switch
        {
            InputEvent.KeyDown k => ui.HandleKeyDown(k.Key, k.Modifiers),
            InputEvent.MouseDown m => ui.HandleMouseDown((int)m.X, (int)m.Y),
            InputEvent.Scroll s => ui.HandleMouseWheel((int)s.Delta),
            _ => (UIResponse.None, System.Collections.Immutable.ImmutableArray<RectInt>.Empty)
        };

        return new PlayerMoveResult(response, clips);
    }
}
