namespace Chess.Lib.UI;

/// <summary>
/// An <see cref="IGamePlayer"/> whose playing strength can be changed while the game is in progress.
///
/// <para>Both engine opponents already re-read their difficulty on every move rather than capturing a
/// search depth once — <c>LocalEnginePlayer</c> builds its <c>AiEngine</c> per move, <c>UciPlayer</c>
/// puts the depth on each <c>go</c> — so honouring a mid-game change costs them nothing. This names
/// that shared capability so <see cref="GameSession.Difficulty"/> can offer it without knowing which
/// kind of engine it is talking to, and so a front-end never has to.</para>
///
/// <para>Deliberately not on <see cref="IGamePlayer"/>: a remote peer is an opponent with no strength
/// to set, and a human is not adjustable at all.</para>
/// </summary>
public interface IAdjustableDifficulty
{
    /// <summary>Strength to use from the next move onwards.</summary>
    Difficulty Difficulty { get; set; }
}
