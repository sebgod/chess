using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text.Json;
using Chess.Lib;

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

    /// <summary>Our anonymous uid, or empty while the cloud is disabled or not yet signed in.</summary>
    public static string Uid { get; private set; } = "";

    /// <summary>True once sign-in has succeeded; false means "no cloud", never "broken".</summary>
    public static bool IsEnabled => Uid.Length > 0;

    /// <summary>
    /// Load the config, the SDK and an anonymous identity, in that order, and stop quietly at the
    /// first one that is absent. Safe to call more than once.
    /// </summary>
    public static async Task<bool> InitAsync(HttpClient http)
    {
        if (IsEnabled) return true;

        // The config is fetched rather than embedded because it is deployment state, not build
        // state: the same wasm serves a fork with no backend and the live site with one.
        string config;
        try
        {
            using var res = await http.GetAsync("firebase-config.json");
            if (!res.IsSuccessStatusCode) return false;
            config = await res.Content.ReadAsStringAsync();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(config)) return false;

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

    /// <summary>
    /// A fresh game id: URL-safe, unguessable enough that nobody stumbles into someone else's game,
    /// and short enough to live in a link. It is NOT a secret — the rules decide who may read a
    /// game, not whether the id can be found — so this only has to avoid collisions.
    /// </summary>
    public static string NewGameId()
    {
        const string Alphabet = "abcdefghijkmnopqrstuvwxyz23456789"; // no l/1/0/o: these get read aloud
        var id = new char[12];
        var bytes = RandomNumberGenerator.GetBytes(id.Length);
        for (var i = 0; i < id.Length; i++) id[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(id);
    }

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
}
