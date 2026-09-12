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
    /// A cloud game id, as in <c>#c=abc123</c> — a link that names a game the cloud courier is
    /// carrying rather than carrying the game itself.
    ///
    /// <para>It is the same grammar on purpose. A cloud game still wants a link ("here, play me"),
    /// and giving it one means the two couriers hand off to each other instead of being two
    /// separate ways in: one address bar, one format, and a link that stays a handful of bytes no
    /// matter how long the game runs. Old builds ignore the key (unknown keys are skipped) and see
    /// a link with no <c>g</c>, which is exactly right — they cannot play it.</para>
    /// </summary>
    public const string CloudKey = "c";

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
    public static string EncodeFragment(Game game) =>
        $"#{GameKey}{KeyValueSeparator}{EncodeMoves(game)}";

    /// <summary>The "#c=…" fragment naming a cloud game — the link that starts one.</summary>
    public static string EncodeCloudFragment(string gameId) =>
        $"#{CloudKey}{KeyValueSeparator}{gameId}";

    /// <summary>
    /// Pulls a cloud game id out of whatever shape a link arrived in, or returns false. Kept here
    /// beside <see cref="ExtractBody"/> rather than in the browser app, for the reason this class
    /// exists at all: one parser, however many front-ends.
    /// </summary>
    public static bool TryExtractCloudId(string? received, out string gameId)
    {
        gameId = "";

        foreach (var pair in ExtractBody(received).Split(ParamSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf(KeyValueSeparator);
            if (idx < 0 || pair[..idx] != CloudKey) continue;

            var value = pair[(idx + 1)..];
            // A game id is ours to mint (see CloudCourier.NewGameId) and lands in a database path,
            // so anything outside the alphabet it is minted from is a link to nothing -- reject it
            // here rather than sending a malformed path to the database.
            if (value.Length is 0 or > 64) return false;
            foreach (var c in value)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') return false;
            }

            gameId = value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The move list alone, with no fragment around it — what the cloud courier stores in a game
    /// row's <c>g</c>. The same payload as <see cref="EncodeFragment"/>, minus a wrapper the
    /// database has no use for.
    ///
    /// <para>It is a separate method rather than a caller stripping three characters, because the
    /// cloud's append-only rule is a <em>string</em> test on this exact value
    /// (<c>newData.val().beginsWith(data.val())</c>): only a string that is nothing but moves stays
    /// prefix-stable when a ply is added. Put the '#g=' wrapper in the row and any future
    /// '&amp;'-separated param would land after the moves, where appending a ply rewrites the
    /// middle of the string and every subsequent write is refused.</para>
    /// </summary>
    public static string EncodeMoves(Game game)
    {
        // Shared move-list helper rebuilds each ply WITH its promotion piece (RecordedPly.Action
        // drops Promoted), so "e7e8q" doesn't degrade to "e7e8".
        var moves = UciMove.FormatMoves(game);
        return string.Join(MoveSeparator, moves);
    }

    /// <summary>
    /// Decodes a bare move list — a cloud row's <c>g</c> — through the same replay and validation
    /// as a link. Exists so the cloud courier doesn't rebuild the format outside this file; the
    /// whole point of the class is that no front-end grows its own parser.
    /// </summary>
    public static GameLinkResult TryDecodeMoves(string moves, out Game? game, out string? error) =>
        TryDecode($"{GameKey}{KeyValueSeparator}{moves}", out game, out error);

    /// <summary>
    /// Reduces whatever shape a link arrived in to the body <see cref="TryDecode"/> parses. A game
    /// can reach a desktop app as a page URL (<c>https://…/chess/#g=e2e4</c>), as a custom-scheme URL
    /// (<c>chess://play?g=e2e4</c>), or as a bare body somebody pasted out of the middle of one —
    /// and all three are the same grammar, because the fragment body is already '&amp;'-separated
    /// <c>key=value</c> pairs with the leading '#' optional. A query string is that same grammar
    /// after a different delimiter, so the whole difference between the three is where the body
    /// starts.
    ///
    /// <para>Fragment wins over query, as in any URL: <c>chess://play?x=1#g=e2e4</c> is a link to the
    /// game in its fragment. Surrounding whitespace goes, because a link pasted from a chat client or
    /// handed over on a command line routinely carries a newline or a stray space.</para>
    ///
    /// <para>This exists so no front-end grows its own parser. <see cref="TryDecode"/> calls it, so
    /// every caller may hand it a whole URL; it is public because a host also needs to ask
    /// "<em>is</em> this argument a link?" before deciding to skip its startup wizard.</para>
    /// </summary>
    public static string ExtractBody(string? received)
    {
        if (string.IsNullOrWhiteSpace(received)) return "";

        var text = received.Trim();

        var hash = text.IndexOf('#');
        if (hash >= 0) return text[(hash + 1)..];

        var query = text.IndexOf('?');
        if (query >= 0) return text[(query + 1)..];

        return text;
    }

    /// <summary>
    /// Parses a URL fragment (leading '#' optional) and replays it into a fresh standard-start
    /// <see cref="Game"/>, validating every move: <see cref="UciMove.Parse"/> then
    /// <see cref="Game.TryMove"/>, aborting on the first token that doesn't parse or isn't legal
    /// in the position reached so far.
    /// </summary>
    /// <summary>
    /// True when <paramref name="candidate"/> <em>continues</em> <paramref name="current"/> rather
    /// than being some other game: every ply already played matches, and the candidate is at least as
    /// long. This is how a host tells "my opponent's reply" from "a different game someone sent me",
    /// which is the difference between quietly updating the board and throwing away a game in
    /// progress.
    ///
    /// <para>Comparing decoded move lists rather than the encoded strings is deliberate. A string
    /// prefix test looks equivalent, but it compares an <em>encoding</em>: it is sensitive to the
    /// variable-length promotion token (<c>"e7e8"</c> is a string prefix of <c>"e7e8q"</c>) and to
    /// the <c>'&amp;'</c>-separated params the format reserves for forward compatibility, neither of
    /// which is the game. Comparing plies cannot be broken by a change to how links are written.</para>
    ///
    /// <para>This is the local half of the same rule the cloud courier enforces server-side as an
    /// append-only history — one policy, two couriers.</para>
    /// </summary>
    public static bool IsContinuationOf(Game current, Game candidate)
    {
        if (candidate.PlyCount < current.PlyCount) return false;

        string[] played = UciMove.FormatMoves(current), incoming = UciMove.FormatMoves(candidate);

        for (var i = 0; i < played.Length; i++)
        {
            if (played[i] != incoming[i]) return false;
        }

        return true;
    }

    public static GameLinkResult TryDecode(string fragment, out Game? game, out string? error)
    {
        game = null;
        error = null;

        // One reduction for every shape a link can arrive in (see ExtractBody) — a plain
        // '#g=…' fragment is simply the case where there is nothing to strip.
        var body = ExtractBody(fragment);
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
