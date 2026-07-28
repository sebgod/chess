using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>
/// The local human on an event-driven front-end: the driver hands it input as it arrives, and
/// <see cref="TryMakeMove"/> applies it on the next tick.
///
/// <para>Chess.Droid and Chess.Web used to call <see cref="GameUI.HandleMouseDown"/> straight from
/// their tap/pointer handlers and throw away the clip rects, which is why neither could use
/// <see cref="GameSession"/>. Queueing turns a push event into something a polled session can ask
/// for, without either side changing how it prefers to work.</para>
///
/// <para>Deliberately one event deep. Input is applied on the very next tick, and a driver that
/// delivers two taps between ticks is either double-tapping or ticking too rarely — in both cases
/// dropping the older event is what the player expects, and it keeps a queue from building up a
/// backlog of stale board coordinates that no longer mean what they did.</para>
/// </summary>
public sealed class QueuedInputPlayer : IGamePlayer
{
    private readonly record struct Pointer(int X, int Y);
    private readonly record struct Key(InputKey Code, InputModifier Modifiers);

    private Pointer? _pointer;
    private Key? _key;

    /// <summary>Queues a tap/click at device-independent content coordinates.</summary>
    public void PressPointer(int x, int y) => _pointer = new Pointer(x, y);

    /// <summary>Queues a key press.</summary>
    public void PressKey(InputKey key, InputModifier modifiers = InputModifier.None) => _key = new Key(key, modifiers);

    /// <summary>True when something is waiting to be applied — a driver can use this to decide it is worth ticking.</summary>
    public bool HasPendingInput => _pointer is not null || _key is not null;

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        // Pointer first: it is the one that carries a board position, so if both landed in the same
        // gap it is the more likely to be the move the player is waiting to see.
        if (_pointer is { } pointer)
        {
            _pointer = null;
            var (response, clips) = ui.HandleMouseDown(pointer.X, pointer.Y);
            return new PlayerMoveResult(response, clips);
        }

        if (_key is { } key)
        {
            _key = null;
            var (response, clips) = ui.HandleKeyDown(key.Code, key.Modifiers);
            return new PlayerMoveResult(response, clips);
        }

        return null;
    }
}
