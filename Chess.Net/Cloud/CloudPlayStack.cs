using System;
using System.Threading.Tasks;
using Chess.Lib;

namespace Chess.Net.Cloud;

/// <summary>
/// The whole cloud-play stack for one visit to the lobby, opened and torn down as a unit — the
/// sibling of <see cref="LanPlayStack"/>, and deliberately the same shape, so a host opens either
/// courier the same way and then talks only to <see cref="ILobby"/>.
///
/// <para><see cref="TryCreate"/> is the "is there a cloud at all" check, and it is one call because
/// every caller does the same thing with every reason there might not be: <b>no backend is a normal
/// state</b>. A fork, a PR build, any checkout that has not opted in has no
/// <c>firebase-config.json</c>, and the answer is to offer LAN and link play, not to report a
/// fault.</para>
///
/// <para>Disposing withdraws the posting and closes the subscriptions, but NOT an established
/// <see cref="NetworkSession"/> — its subscription is its own, so the lobby that produced a session is
/// torn down while the game plays on, which is what every host does.</para>
/// </summary>
public sealed class CloudPlayStack : IAsyncDisposable
{
    private readonly FirebaseDatabase _db;

    private CloudPlayStack(FirebaseConfig config, string localName, Side preferredColor, TimeProvider time)
    {
        _db = new FirebaseDatabase(config, time: time);
        Lobby = new CloudLobby(_db, localName, preferredColor, time);
    }

    /// <summary>
    /// The stack for this deployment's backend, or null when there isn't one.
    /// </summary>
    /// <param name="configJson">The contents of <c>firebase-config.json</c> — deployment state, so it
    /// is read at runtime rather than baked in: the same binary serves a checkout with no backend and
    /// the real thing with one.</param>
    public static CloudPlayStack? TryCreate(
        string? configJson, string localName, Side preferredColor, TimeProvider? time = null) =>
        FirebaseConfig.TryParse(configJson) is { } config
            ? new CloudPlayStack(config, localName, preferredColor, time ?? TimeProvider.System)
            : null;

    /// <summary>The lobby to drive: <c>Start()</c>, poll <c>State</c>/<c>Peers</c>, then take
    /// <c>Session</c> once it reaches <see cref="LobbyState.Connected"/>.</summary>
    public ILobby Lobby { get; }

    public async ValueTask DisposeAsync()
    {
        // Order matters: the lobby's withdrawal is a database write, so it has to go out before the
        // client carrying it is shut down.
        await Lobby.DisposeAsync().ConfigureAwait(false);
        await _db.DisposeAsync().ConfigureAwait(false);
    }
}
