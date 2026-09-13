using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Net.Cloud;

/// <summary>
/// The database the cloud courier talks to, as the courier needs it: read a path, write a path,
/// subscribe to a path. <see cref="FirebaseDatabase"/> is the real one (REST + server-sent events over
/// <c>HttpClient</c>); tests use an in-memory stand-in, which is the same trick
/// <c>Chess.Tests/Lan/FakeLanBus.cs</c> plays for the LAN — and the reason the cloud courier needs no
/// cloud to test.
///
/// <para><b>Nothing here knows chess</b>, deliberately, exactly as the browser's
/// <c>wwwroot/js/firebase-cloud.js</c> knows none. The schema and the rules are the boundary: they
/// enforce append-only, the turn gate and seat ownership, so a bug on this side can fail to show a
/// game but cannot corrupt one.</para>
///
/// <para>Paths are database paths with no leading slash — <c>games/abc123</c>, <c>open</c>,
/// <c>invites/{uid}</c> — and values are raw JSON, which is what both ends already speak. There is no
/// serializer in the middle: <c>Chess.Net</c> is <c>IsAotCompatible</c>, and the reflection-based
/// <c>JsonSerializer</c> would end that. Reading goes through <c>JsonDocument</c> and writing through
/// string building, neither of which reflects over anything.</para>
/// </summary>
public interface ICloudDatabase : IAsyncDisposable
{
    /// <summary>Our identity, which every rule in the database is written against. Empty until
    /// <see cref="SignInAsync"/> has succeeded.</summary>
    string Uid { get; }

    /// <summary>Take an identity. Anonymous by default — see the identity section of
    /// docs/correspondence-play.md for why that is the default and not a limitation. Returns false
    /// when there is no cloud to sign in to, which is a normal state and never an exception.</summary>
    Task<bool> SignInAsync(CancellationToken ct = default);

    /// <summary>The JSON at <paramref name="path"/>, or null when it is absent or not ours to
    /// read — the two are deliberately one answer, because a rule saying "not your game" and a game
    /// that does not exist call for the same thing from a caller.</summary>
    Task<string?> GetAsync(string path, CancellationToken ct = default);

    /// <summary>Replace the value at <paramref name="path"/>. False means the rules refused it, which
    /// is a legitimate outcome (someone claimed the seat first) rather than a failure.</summary>
    Task<bool> SetAsync(string path, string json, CancellationToken ct = default);

    /// <summary>Merge <paramref name="json"/>'s members into the value at <paramref name="path"/>,
    /// leaving the others alone. This is how a ply is appended: <c>g</c> and <c>n</c> move together,
    /// and the rules reject any <c>g</c> that is not an extension of what is stored.</summary>
    Task<bool> UpdateAsync(string path, string json, CancellationToken ct = default);

    /// <summary>Remove the value at <paramref name="path"/>.</summary>
    Task<bool> RemoveAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Subscribe to <paramref name="path"/>. <paramref name="onValue"/> is handed the whole value
    /// there as JSON on every change <b>including the first</b>, and null when it is absent or not
    /// ours — the same contract the browser's <c>onValue</c> has, so both couriers are reasoned about
    /// the same way. Delivery is off the caller's thread. Dispose the handle to unsubscribe.
    /// </summary>
    IDisposable Watch(string path, Action<string?> onValue);
}
