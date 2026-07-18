namespace Chess.UCI;

/// <summary>Outcome of <see cref="GameLinkCodec.TryDecode"/>.</summary>
public enum GameLinkResult
{
    /// <summary>
    /// The fragment has no recognized game key — not a game link at all (a bare visit, or an
    /// unrelated hash). Callers should fall back to the normal startup flow silently; this is
    /// not an error.
    /// </summary>
    NoLink,

    /// <summary>
    /// A game key was present but rejected — bad UCI token, illegal move, an unsupported
    /// required param (e.g. a future "f=" this build doesn't know), or too many plies. Callers
    /// should surface the error text and fall back without applying anything.
    /// </summary>
    Invalid,

    /// <summary>Successfully replayed from the standard start; the decoded game is valid.</summary>
    Ok,
}
