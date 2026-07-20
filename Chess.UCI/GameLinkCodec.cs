using Chess.Lib;
using Action = Chess.Lib.Action;

namespace Chess.UCI;

/// <summary>
/// Encodes/decodes the URL-fragment format behind "Play by Link" correspondence chess: the whole
/// game travels inside a shareable link (<c>#g=e2e4.e7e5.g1f3</c>), so two players can exchange
/// moves over any messenger with no server, accounts, or storage — the URL is the save file.
///
/// <para>The payload is a replay log, not a position snapshot, by necessity: castling and
/// en-passant rights are derived from ply <em>history</em> (see <c>Board.ValidateCastling</c>),
/// so a FEN of the current position would silently lose them. Replaying through
/// <see cref="Game.TryMove"/> also validates every ply, making the rules engine the parser's
/// watchdog — a corrupted or hand-tampered link cannot produce an illegal position.</para>
///
/// <para>Moves are dot-separated UCI: promotions are 5 chars ("a7a8b") and 'b'/'n' are also
/// file/board letters, so concatenation without a separator would be ambiguous. The fragment
/// body parses as '&amp;'-separated key=value pairs; unknown keys are ignored (forward compat)
/// except <see cref="PlacementKey"/>, which is explicitly rejected so this version never
/// mis-plays a future custom-start link as a standard-start game.</para>
/// </summary>
public static class GameLinkCodec
{
    public const string GameKey = "g";

    /// <summary>Reserved for a future custom-start-position param; always rejected in v1.</summary>
    public const string PlacementKey = "f";

    /// <summary>
    /// Generous upper bound on plies a link may encode — bounds the replay work a hostile
    /// fragment can demand; no human game comes anywhere close.
    /// </summary>
    public const int MaxPlies = 4096;

    private const char ParamSeparator = '&';
    private const char KeyValueSeparator = '=';
    private const char MoveSeparator = '.';

    /// <summary>
    /// Builds the "#g=…" fragment (leading '#' included, ready for history.replaceState or
    /// concatenation onto a base URL) for the game's played plies. An unstarted game encodes as
    /// "#g=" — the start link a Black-playing creator sends so their opponent opens as White.
    /// </summary>
    public static string EncodeFragment(Game game)
    {
        // Shared move-list helper rebuilds each ply WITH its promotion piece (RecordedPly.Action
        // drops Promoted), so "e7e8q" doesn't degrade to "e7e8".
        var moves = UciMove.FormatMoves(game);
        return $"#{GameKey}{KeyValueSeparator}{string.Join(MoveSeparator, moves)}";
    }

    /// <summary>
    /// Parses a URL fragment (leading '#' optional) and replays it into a fresh standard-start
    /// <see cref="Game"/>, validating every move: <see cref="UciMove.Parse"/> then
    /// <see cref="Game.TryMove"/>, aborting on the first token that doesn't parse or isn't legal
    /// in the position reached so far.
    /// </summary>
    public static GameLinkResult TryDecode(string fragment, out Game? game, out string? error)
    {
        game = null;
        error = null;

        var body = fragment.StartsWith('#') ? fragment[1..] : fragment;
        string? movesPart = null;

        foreach (var pair in body.Split(ParamSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf(KeyValueSeparator);
            var key = idx < 0 ? pair : pair[..idx];
            var value = idx < 0 ? "" : pair[(idx + 1)..];

            if (key == PlacementKey)
            {
                error = "custom start-position links aren't supported by this version";
                return GameLinkResult.Invalid;
            }

            if (key == GameKey)
            {
                movesPart = value;
            }
            // any other key: ignored — new optional params must not break old builds
        }

        if (movesPart is null)
        {
            return GameLinkResult.NoLink;
        }

        var tokens = movesPart.Split(MoveSeparator, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length > MaxPlies)
        {
            error = $"link encodes too many moves ({tokens.Length} > {MaxPlies})";
            return GameLinkResult.Invalid;
        }

        var replay = new Game();

        for (var i = 0; i < tokens.Length; i++)
        {
            Action action;
            try
            {
                action = UciMove.Parse(tokens[i]);
            }
            catch (FormatException ex)
            {
                error = $"move #{i + 1} ('{tokens[i]}') is not valid UCI: {ex.Message}";
                return GameLinkResult.Invalid;
            }

            var result = replay.TryMove(action);
            if (!result.IsMoveOrCapture())
            {
                error = $"move #{i + 1} ('{tokens[i]}') is illegal in this position ({result})";
                return GameLinkResult.Invalid;
            }
        }

        game = replay;
        return GameLinkResult.Ok;
    }
}
