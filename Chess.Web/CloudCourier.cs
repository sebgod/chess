using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Chess.Lib;
using Chess.UCI;

namespace Chess.Web;

/// <summary>
/// The cloud courier: the same payload the link courier carries, delivered by a database instead of
/// by the user. <c>g</c> in a row is exactly what <c>GameLinkCodec.EncodeFragment</c> produces, so a
/// game arriving from the cloud is decoded by the code that already decodes a pasted link — there is
/// no second format, no message protocol, and nothing here that knows chess.
///
/// <para>That is why the browser needs neither <c>Chess.Net</c> nor <c>SessionProtocol</c>: a cloud
/// game is not a stream of moves between two peers, it is one shared STATE that both sides extend.
/// Chess is perfect-information, so the state is the whole game and the server can stay dumb (see
/// docs/correspondence-play.md).</para>
///
/// <para><see cref="JSImport"/> rather than <c>IJSRuntime</c>, matching WebGl.Renderer's command
/// buffer: the calls are typed, source-generated and trim-safe, which matters in a project that AOT
/// publishes. The JS half is <c>wwwroot/js/firebase-cloud.js</c>, and
/// <c>firebase/cloud-check.html</c> exercises that half on its own — so a failure here can be
/// bisected to one side or the other rather than "the cloud doesn't work".</para>
///
/// <para><b>Disabled is a normal state, not an error.</b> With no <c>firebase-config.json</c> —
/// every fork, every PR build, any local checkout that hasn't opted in — <see cref="InitAsync"/>
/// returns false, nothing is fetched, and the app plays link, hot-seat and vs-computer games
/// exactly as before. Callers check <see cref="IsEnabled"/>; nothing throws.</para>
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class CloudCourier
{
    private const string Module = "firebase-cloud";

    private static bool _moduleLoaded;
    private static string? _config;     // fetched once; null until asked, "" once known absent

    /// <summary>Our anonymous uid, or empty while the cloud is disabled or not yet signed in.</summary>
    public static string Uid { get; private set; } = "";

    /// <summary>True once sign-in has succeeded; false means "no cloud", never "broken".</summary>
    public static bool IsEnabled => Uid.Length > 0;

    /// <summary>
    /// Whether this deployment has a backend at all — one small fetch, and nothing else. It loads
    /// no SDK and creates no identity, which is the point: every visitor asks this (the menu offers
    /// online play or it does not), and only the ones who choose it should cost an anonymous user.
    ///
    /// <para>The config is fetched rather than embedded because it is deployment state, not build
    /// state: the same wasm serves a fork with no backend and the live site with one.</para>
    /// </summary>
    public static async Task<bool> IsConfiguredAsync(HttpClient http)
    {
        if (_config is not null) return _config.Length > 0;

        try
        {
            using var res = await http.GetAsync("firebase-config.json");
            _config = res.IsSuccessStatusCode ? (await res.Content.ReadAsStringAsync()).Trim() : "";
        }
        catch
        {
            _config = "";
        }

        // A dev server with no such file answers 200 and an HTML fallback rather than 404, so the
        // shape is checked too: a config is an object, and "<!DOCTYPE html>" is not one.
        if (!_config.StartsWith('{')) _config = "";

        return _config.Length > 0;
    }

    /// <summary>
    /// Load the SDK and take an anonymous identity. Call it when the player asks for online play,
    /// not at boot. Safe to call more than once.
    /// </summary>
    public static async Task<bool> InitAsync(HttpClient http)
    {
        if (IsEnabled) return true;
        if (!await IsConfiguredAsync(http)) return false;

        var config = _config!;

        try
        {
            if (!_moduleLoaded)
            {
                // Relative to the IMPORTING module, which Blazor serves out of `_framework/` -- so
                // "./js/..." resolves to `_framework/js/...` and 404s. Climbing one level lands on
                // the app base in both layouts that matter: `/js/...` locally and `/chess/js/...`
                // under the project-site subpath, without hard-coding either.
                await JSHost.ImportAsync(Module, "../js/firebase-cloud.js");
                _moduleLoaded = true;
            }

            Uid = await InitJs(config);
        }
        catch (Exception ex)
        {
            // A module that will not load, an SDK the network will not serve, a config the SDK
            // rejects: all of them mean "no cloud", and none of them is a reason for the chess app
            // to stop. Report and carry on disabled.
            Console.Error.WriteLine($"[chess-web] cloud init failed, continuing without it: {ex.Message}");
            Uid = "";
        }

        return IsEnabled;
    }

    /// <summary>
    /// Watch one game. <paramref name="onRow"/> is called on every change including the first, with
    /// the row as JSON, or null when the game is absent or not ours to read. Watching a second game
    /// replaces the first.
    /// </summary>
    public static void Watch(string gameId, Action<string?> onRow)
    {
        if (!IsEnabled) return;
        WatchJs(gameId, onRow);
    }

    public static void Unwatch(string gameId)
    {
        if (!IsEnabled) return;
        UnwatchJs(gameId);
    }

    /// <summary>
    /// Extend the move log. False means the rules refused it — not our turn, not our game, or
    /// someone else got there first — which is a legitimate outcome, not an exception: the server
    /// is the arbiter of whose turn it is, and a client that disagrees is the one that is wrong.
    /// </summary>
    public static Task<bool> AppendAsync(string gameId, string moves, int plyCount)
        => IsEnabled ? AppendJs(gameId, moves, plyCount) : Task.FromResult(false);

    /// <summary>Create a game with us in one seat. <paramref name="color"/> is "w" or "b".</summary>
    public static Task<bool> CreateAsync(string gameId, string color, string name)
        => IsEnabled ? CreateJs(gameId, color, name) : Task.FromResult(false);

    /// <summary>Take the empty seat of someone else's game — the cloud's Accept.</summary>
    public static Task<bool> ClaimAsync(string gameId, string color, string name)
        => IsEnabled ? ClaimJs(gameId, color, name) : Task.FromResult(false);

    /// <summary>Advertise a game with an empty seat. One per player, by construction.</summary>
    public static Task<bool> PostAsync(string gameId, string name, string color)
        => IsEnabled ? PostJs(gameId, name, color) : Task.FromResult(false);

    /// <summary>Withdraw our open game.</summary>
    public static Task<bool> UnpostAsync()
        => IsEnabled ? UnpostJs() : Task.FromResult(false);

    /// <summary>Watch the lobby; <paramref name="onList"/> gets the rows as a JSON array.</summary>
    public static void WatchOpen(Action<string?> onList)
    {
        if (IsEnabled) WatchOpenJs(onList);
    }

    public static void UnwatchOpen()
    {
        if (IsEnabled) UnwatchOpenJs();
    }

    /// <summary>
    /// A lobby row: somebody's open game. <c>Host</c> is the poster's uid, which is also the row's
    /// key — so a player can hold at most one of these.
    /// </summary>
    public sealed record OpenGame(string Host, string GameId, string Name, string Color, long Updated)
    {
        /// <summary>The colour the POSTER took; whoever joins plays the other one.</summary>
        public Side PosterSide => Color == "b" ? Side.Black : Side.White;

        public Side JoinerSide => PosterSide.ToOpposite();

        /// <summary>
        /// Rows go stale rather than expiring: a posted game outlives the tab that posted it (that
        /// is what makes correspondence possible), so nothing on the server removes an abandoned
        /// one. The same 120 days the desktop inbox uses to hide a dead game hides a dead posting.
        /// </summary>
        public bool IsStale(DateTimeOffset now) =>
            Updated > 0 && now - DateTimeOffset.FromUnixTimeMilliseconds(Updated) > TimeSpan.FromDays(120);
    }

    public static List<OpenGame> ParseOpen(string? json)
    {
        var rows = new List<OpenGame>();
        if (string.IsNullOrEmpty(json)) return rows;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return rows;

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;

                static string Str(JsonElement row, string key) =>
                    row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() ?? "" : "";

                var host = Str(row, "host");
                var gameId = Str(row, "gameId");
                if (host.Length == 0 || gameId.Length == 0) continue;

                rows.Add(new OpenGame(host, gameId, Str(row, "name"), Str(row, "color"),
                    row.TryGetProperty("updated", out var u) && u.TryGetInt64(out var ms) ? ms : 0));
            }
        }
        catch (JsonException)
        {
            // A malformed lobby is an empty lobby: it is a list of other people's rows, and no
            // single bad one should cost the player the screen.
        }

        return rows;
    }

    /// <summary>
    /// A game row: the move log, the ply count, and who holds the two seats. <c>Moves</c> is a bare
    /// move list — <c>GameLinkCodec.TryDecodeMoves</c> turns it into a game.
    /// </summary>
    public sealed record Row(string Moves, int PlyCount, string? WhiteUid, string? BlackUid,
                             string? WhiteName, string? BlackName)
    {
        /// <summary>Which seat we hold, or null if we hold neither.</summary>
        public Side? SeatOf(string uid) =>
            WhiteUid == uid ? Side.White : BlackUid == uid ? Side.Black : null;

        /// <summary>Who holds a seat, or null while it is still empty.</summary>
        public string? UidOf(Side side) => side == Side.White ? WhiteUid : BlackUid;

        /// <summary>
        /// The name on a seat. Optional even when the seat is taken — a name is something a player
        /// types, so "nobody has sat down yet" and "they did not say who they are" are different
        /// answers and the caller has to tell them apart (see <see cref="UidOf"/>).
        /// </summary>
        public string? NameOf(Side side) => side == Side.White ? WhiteName : BlackName;
    }

    public static Row? ParseRow(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            static (string? Uid, string? Name) Seat(JsonElement root, string key) =>
                root.TryGetProperty(key, out var seat) && seat.ValueKind == JsonValueKind.Object
                    ? (seat.TryGetProperty("uid", out var u) ? u.GetString() : null,
                       seat.TryGetProperty("name", out var n) ? n.GetString() : null)
                    : (null, null);

            var (wUid, wName) = Seat(root, "w");
            var (bUid, bName) = Seat(root, "b");

            return new Row(
                root.TryGetProperty("g", out var gv) ? gv.GetString() ?? "" : "",
                root.TryGetProperty("n", out var nv) && nv.TryGetInt32(out var n) ? n : 0,
                wUid, bUid, wName, bName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A fresh game id. The grammar lives with the rest of the link format in
    /// <see cref="GameLinkCodec"/>, because the native courier mints these too and two copies of an
    /// id alphabet drift into a game only one front-end can name.</summary>
    public static string NewGameId() => GameLinkCodec.NewCloudGameId();

    [JSImport("init", Module)]
    private static partial Task<string> InitJs(string configJson);

    [JSImport("watch", Module)]
    private static partial void WatchJs(
        string gameId,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string?> onRow);

    [JSImport("unwatch", Module)]
    private static partial void UnwatchJs(string gameId);

    [JSImport("append", Module)]
    private static partial Task<bool> AppendJs(string gameId, string moves, int plyCount);

    [JSImport("create", Module)]
    private static partial Task<bool> CreateJs(string gameId, string color, string name);

    [JSImport("claim", Module)]
    private static partial Task<bool> ClaimJs(string gameId, string color, string name);

    [JSImport("post", Module)]
    private static partial Task<bool> PostJs(string gameId, string name, string color);

    [JSImport("unpost", Module)]
    private static partial Task<bool> UnpostJs();

    [JSImport("watchOpen", Module)]
    private static partial void WatchOpenJs(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string?> onList);

    [JSImport("unwatchOpen", Module)]
    private static partial void UnwatchOpenJs();
}
