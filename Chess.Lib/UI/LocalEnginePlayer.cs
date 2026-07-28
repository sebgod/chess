using System.Collections.Immutable;
using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>
/// The engine as an opponent, searching in-process on the calling thread.
///
/// <para>The counterpart to <c>Chess.UCI.UciPlayer</c>, which drives a separate engine process and can
/// answer "not yet". This one has no process to wait on: when asked on its turn it searches and
/// returns a move, so <see cref="TryMakeMove"/> blocks for as long as the search takes. That is what
/// Chess.Droid and Chess.Web already did before they shared a session — Droid from its SDL frame
/// callback, Web on its single WASM thread — and it is why <see cref="Difficulty"/> caps out at a
/// depth that stays answerable on those hosts.</para>
///
/// <para>A front-end that cannot afford to freeze while this runs should check
/// <see cref="GameSession.IsEngineTurn"/> and paint first; once the call has started it is too
/// late.</para>
/// </summary>
public sealed class LocalEnginePlayer(Side side, Difficulty difficulty = Difficulty.Normal) : IGamePlayer
{
    /// <summary>The side this engine plays.</summary>
    public Side Side { get; } = side;

    /// <summary>How deep it searches. Settable so a front-end can offer a mid-game change.</summary>
    public Difficulty Difficulty { get; set; } = difficulty;

    public PlayerMoveResult? TryMakeMove(GameUI ui)
    {
        var game = ui.Game;

        // The guard that makes an off-turn poll safe — GameSession asks every opponent on every tick
        // so a remote peer's departure is noticed promptly, and relies on this to not move out of turn.
        if (game.CurrentSide != Side || game.IsFinished || ui.IsSetupMode)
        {
            return null;
        }

        var move = new AiEngine(Side, Difficulty.ToSearchDepth()).PickMove(game);

        if (move is not { } action)
        {
            return null;
        }

        var (response, clips) = ui.TryPerformAction(action);
        return new PlayerMoveResult(response, clips);
    }
}
