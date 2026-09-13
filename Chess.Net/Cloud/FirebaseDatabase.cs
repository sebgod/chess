using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Net.Cloud;

/// <summary>
/// The real <see cref="ICloudDatabase"/>: Realtime Database over its REST API, with server-sent
/// events for push. <b>No Firebase SDK</b> — there is no good AOT-friendly .NET client, and
/// <c>Chess.Net</c> deliberately carries zero packages beyond LAN.Lib. It does not need one: appending
/// <c>.json</c> to a path reads or writes it, and the same GET with
/// <c>Accept: text/event-stream</c> becomes a change stream. That is <c>HttpClient</c> and a line
/// reader, which is the whole dependency.
///
/// <para>Anonymous sign-in is likewise one HTTPS POST to Identity Toolkit. The token it returns lasts
/// an hour and is refreshed here on demand, which is not optional for this app: a correspondence game
/// sits idle for hours by design, so the interesting case is always the one where the token expired
/// long before anyone next touched it.</para>
///
/// <para><b>Reconnects are silent.</b> A dropped stream, a DNS failure, a phone that changed networks:
/// none of those mean the game is gone, and reporting them as absence would unwind the host to the
/// menu over a passing wifi blip. Only the server actually saying so — a null value, or a read the
/// rules refuse — delivers null. Everything else backs off and tries again, and on reconnect the
/// server re-sends the whole value, so no ply can be missed by having been offline for it.</para>
/// </summary>
public sealed class FirebaseDatabase : ICloudDatabase
{
    // Refresh this far ahead of expiry: far enough that a request in flight is never the one to
    // discover the token died, near enough not to churn tokens on an idle app.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly FirebaseConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private string _idToken = "";
    private string _refreshToken = "";
    private DateTimeOffset _expiresAt;

    public string Uid { get; private set; } = "";

    /// <param name="http">Shared client, or null to own one. Its timeout is set to infinite either
    /// way — an event stream is a response that never completes, and the default 100 seconds would
    /// cut it — so every request here carries its own <see cref="RequestTimeout"/> instead.</param>
    public FirebaseDatabase(FirebaseConfig config, HttpClient? http = null, TimeProvider? time = null)
    {
        _config = config;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _time = time ?? TimeProvider.System;
    }

    // ---- identity ---------------------------------------------------------------------------

    private string IdentityBase => _config.IsEmulator
        ? $"{_config.EmulatorAuthUrl}/identitytoolkit.googleapis.com/v1"
        : "https://identitytoolkit.googleapis.com/v1";

    private string SecureTokenBase => _config.IsEmulator
        ? $"{_config.EmulatorAuthUrl}/securetoken.googleapis.com/v1"
        : "https://securetoken.googleapis.com/v1";

    public async Task<bool> SignInAsync(CancellationToken ct = default)
    {
        if (Uid.Length > 0) return true;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
        linked.CancelAfter(RequestTimeout);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{IdentityBase}/accounts:signUp?key={Uri.EscapeDataString(_config.ApiKey)}")
            {
                Content = new StringContent("{\"returnSecureToken\":true}", Encoding.UTF8, "application/json"),
            };
            using var res = await _http.SendAsync(req, linked.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return false;

            using var doc = CloudJson.TryParse(
                await res.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false));
            if (doc is null) return false;

            var root = doc.RootElement;
            var uid = CloudJson.StringOr(root, "localId");
            var idToken = CloudJson.StringOr(root, "idToken");
            if (uid.Length == 0 || idToken.Length == 0) return false;

            Uid = uid;
            _idToken = idToken;
            _refreshToken = CloudJson.StringOr(root, "refreshToken");
            _expiresAt = ExpiryFrom(CloudJson.StringOr(root, "expiresIn"));
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            // No network, no backend, a key the project refuses: all of them mean "no cloud right
            // now", which is a state this app has a menu for rather than an exception to propagate.
            return false;
        }
    }

    private DateTimeOffset ExpiryFrom(string seconds) =>
        _time.GetUtcNow() + TimeSpan.FromSeconds(int.TryParse(seconds, out var s) ? s : 3600);

    /// <summary>A token good for the next few minutes, refreshing first if it is not.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_idToken.Length == 0) return "";
        if (_time.GetUtcNow() + RefreshMargin < _expiresAt) return _idToken;
        if (_refreshToken.Length == 0) return _idToken;

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed it while we queued on the gate.
            if (_time.GetUtcNow() + RefreshMargin < _expiresAt) return _idToken;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            linked.CancelAfter(RequestTimeout);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{SecureTokenBase}/token?key={Uri.EscapeDataString(_config.ApiKey)}")
            {
                Content = new StringContent(
                    $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(_refreshToken)}",
                    Encoding.UTF8, "application/x-www-form-urlencoded"),
            };
            using var res = await _http.SendAsync(req, linked.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return _idToken; // let the request fail and be retried

            using var doc = CloudJson.TryParse(
                await res.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false));
            if (doc is null) return _idToken;

            var root = doc.RootElement;
            var idToken = CloudJson.StringOr(root, "id_token");
            if (idToken.Length == 0) return _idToken;

            _idToken = idToken;
            var refreshed = CloudJson.StringOr(root, "refresh_token");
            if (refreshed.Length > 0) _refreshToken = refreshed;
            _expiresAt = ExpiryFrom(CloudJson.StringOr(root, "expires_in"));
            return _idToken;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            return _idToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    // ---- paths ------------------------------------------------------------------------------

    /// <summary>
    /// The REST URL for a path. The emulator is not merely a different host: it serves every
    /// namespace on one port and picks between them with an <c>ns</c> query parameter, where a real
    /// deployment puts the namespace in the hostname.
    /// </summary>
    private string Url(string path, string token)
    {
        var origin = _config.IsEmulator
            ? $"http://{_config.EmulatorDatabaseHost}:{_config.EmulatorDatabasePort}"
            : _config.DatabaseUrl;

        var query = new StringBuilder();
        if (_config.IsEmulator)
            query.Append("ns=").Append(Uri.EscapeDataString($"{_config.ProjectId}-default-rtdb"));
        if (token.Length > 0)
        {
            if (query.Length > 0) query.Append('&');
            query.Append("auth=").Append(Uri.EscapeDataString(token));
        }

        return query.Length == 0 ? $"{origin}/{path}.json" : $"{origin}/{path}.json?{query}";
    }

    // ---- reads and writes -------------------------------------------------------------------

    public async Task<string?> GetAsync(string path, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        // "null" is how the REST API spells an absent value, and a refused read arrives as a failure;
        // one answer covers both, as the interface promises.
        return body is null or "null" ? null : body;
    }

    public async Task<bool> SetAsync(string path, string json, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, path, json, ct).ConfigureAwait(false) is not null;

    public async Task<bool> UpdateAsync(string path, string json, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Patch, path, json, ct).ConfigureAwait(false) is not null;

    public async Task<bool> RemoveAsync(string path, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, path, null, ct).ConfigureAwait(false) is not null;

    private async Task<string?> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
        linked.CancelAfter(RequestTimeout);

        try
        {
            var token = await TokenAsync(linked.Token).ConfigureAwait(false);
            using var req = new HttpRequestMessage(method, Url(path, token));
            if (body is not null)
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, linked.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    // ---- push -------------------------------------------------------------------------------

    public IDisposable Watch(string path, Action<string?> onValue)
    {
        var subscription = new Subscription(this, path, onValue);
        subscription.Start();
        return subscription;
    }

    /// <summary>
    /// One live subscription: an event stream, re-established as often as it takes, folded into the
    /// current value at the path.
    ///
    /// <para>The folding is the only interesting part. The server does not resend the whole value on
    /// every change — it sends <c>put</c> ("this subtree is now that") and <c>patch</c> ("these
    /// members changed") against a path relative to the subscription — so keeping the caller's
    /// contract of "here is the whole value" means maintaining it on this side. Every shape this
    /// courier subscribes to changes at its top level (a game's <c>{g, n}</c>, one row of the lobby),
    /// which is why exactly those two cases are folded and anything deeper re-reads the path instead.
    /// The fallback costs one small GET and cannot be wrong, which is the right trade for a case that
    /// does not arise.</para>
    /// </summary>
    private sealed class Subscription : IDisposable
    {
        private readonly FirebaseDatabase _owner;
        private readonly string _path;
        private readonly Action<string?> _onValue;
        private readonly CancellationTokenSource _cts;

        private string? _value;

        public Subscription(FirebaseDatabase owner, string path, Action<string?> onValue)
        {
            _owner = owner;
            _path = path;
            _onValue = onValue;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(owner._shutdown.Token);
        }

        public void Start() => _ = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);

        private async Task RunAsync(CancellationToken ct)
        {
            var backoff = MinBackoff;

            while (!ct.IsCancellationRequested)
            {
                var ran = false;
                try
                {
                    ran = await StreamAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
                {
                    // Transient. NOT "the game is gone" — say nothing to the caller and try again.
                }

                if (ct.IsCancellationRequested) return;
                backoff = ran ? MinBackoff : Min(backoff * 2, MaxBackoff);
                try { await Task.Delay(backoff, _owner._time, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        /// <summary>One connection's worth of stream. True if it delivered anything before ending,
        /// which resets the backoff; false if it never got going.</summary>
        private async Task<bool> StreamAsync(CancellationToken ct)
        {
            var token = await _owner.TokenAsync(ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Get, _owner.Url(_path, token));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var res = await _owner._http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound)
            {
                // The server has spoken about the value itself: it is not there, or not ours. That is
                // the one case the caller is told about.
                Publish(null);
                return false;
            }
            if (!res.IsSuccessStatusCode) return false;

            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var eventName = "";
            var delivered = false;

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break; // the server closed the stream; reconnect

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line[6..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    delivered = true;
                    await HandleAsync(eventName, line[5..].Trim(), ct).ConfigureAwait(false);
                }
                // Anything else is a comment, a blank frame separator, or a field we do not use.
            }

            return delivered;
        }

        private async Task HandleAsync(string eventName, string data, CancellationToken ct)
        {
            switch (eventName)
            {
                case "put":
                case "patch":
                    break;

                case "cancel":
                    // The rules stopped letting us read this path mid-stream.
                    Publish(null);
                    return;

                case "auth_revoked":
                    // The token expired under us. The loop reconnects, and TokenAsync refreshes first.
                    return;

                default: // keep-alive, and whatever the server adds later
                    return;
            }

            using var doc = CloudJson.TryParse(data);
            if (doc is null) return;

            var relative = CloudJson.StringOr(doc.RootElement, "path", "/");
            var payload = CloudJson.Child(doc.RootElement, "data");
            var payloadJson = payload.ValueKind == JsonValueKind.Undefined ? "null" : payload.GetRawText();

            if (relative == "/")
            {
                if (eventName == "put")
                {
                    Publish(payloadJson == "null" ? null : payloadJson);
                    return;
                }

                var merged = CloudJson.Merge(_value, payloadJson);
                if (merged is not null) { Publish(merged); return; }
            }
            else if (eventName == "put" && relative.LastIndexOf('/') == 0)
            {
                var updated = CloudJson.SetMember(_value, relative[1..], payloadJson);
                if (updated is not null) { Publish(updated); return; }
            }

            // Deeper than we fold, or a shape that would not merge: read the truth rather than
            // reconstruct it.
            Publish(await _owner.GetAsync(_path, ct).ConfigureAwait(false));
        }

        private void Publish(string? value)
        {
            _value = value;
            try { _onValue(value); }
            catch { /* a handler that throws must not take the stream down with it */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _tokenGate.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
