using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chess.Net.Cloud;

namespace Chess.Tests.Cloud;

/// <summary>
/// An in-memory stand-in for the Realtime Database, so the cloud courier can be tested with no cloud
/// — the same trick <see cref="Lan.FakeLanBus"/> plays for the LAN, and for the same reason: the
/// interesting logic is the client's, and a test that needs a network is a test nobody runs.
///
/// <para>It stores what the real database stores: a tree of <b>leaves</b>, flattened here to
/// <c>games/abc/g</c> → <c>"e2e4"</c>. That is not a simplification, it is the semantic that bites —
/// an empty object does not exist in RTDB, a path with no leaves under it reads as null, and a
/// <c>set</c> replaces a subtree rather than merging into it. Modelling paths as opaque keys would
/// have hidden all three.</para>
///
/// <para>Delivery to watchers is <b>synchronous</b>, on the writing thread, which makes an exchange
/// between two clients deterministic and thread-free in a test. Two databases sharing one
/// <see cref="Store"/> are two clients of one backend.</para>
/// </summary>
internal sealed class FakeCloudDatabase(FakeCloudStore store, string uid) : ICloudDatabase
{
    public string Uid { get; private set; } = "";

    /// <summary>Identity taken at sign-in, as the real one is. Tests that skip sign-in see an empty
    /// uid, which is what an unauthenticated client really has.</summary>
    public Task<bool> SignInAsync(CancellationToken ct = default)
    {
        Uid = uid;
        return Task.FromResult(true);
    }

    /// <summary>Refuse a write, standing in for the security rules — "not your turn", "seat already
    /// taken". Handed the path and the JSON; returning true rejects it, exactly as a rule would,
    /// with nothing written and no watcher notified.</summary>
    public Func<string, string?, bool>? Refuse
    {
        get => store.Refuse;
        set => store.Refuse = value;
    }

    public Task<string?> GetAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(store.Read(path));

    public Task<bool> SetAsync(string path, string json, CancellationToken ct = default) =>
        Task.FromResult(store.Set(path, json));

    public Task<bool> UpdateAsync(string path, string json, CancellationToken ct = default) =>
        Task.FromResult(store.Update(path, json));

    public Task<bool> RemoveAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(store.Remove(path));

    public IDisposable Watch(string path, Action<string?> onValue) => store.Watch(path, onValue);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>The backend itself — one per test, shared by however many clients the test needs.</summary>
internal sealed class FakeCloudStore
{
    // Leaf path -> the JSON scalar at it. Sorted so a reconstructed object's members come out in a
    // stable order, which keeps assertions on raw JSON readable.
    private readonly SortedDictionary<string, string> _leaves = new(StringComparer.Ordinal);
    private readonly List<Subscription> _watches = [];

    public Func<string, string?, bool>? Refuse { get; set; }

    /// <summary>Every write the store accepted, for asserting what a client actually sent.</summary>
    public List<(string Path, string? Json)> Writes { get; } = [];

    public string? Read(string path)
    {
        var prefix = path + "/";
        if (_leaves.TryGetValue(path, out var scalar)) return scalar;

        var under = _leaves.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (under.Length == 0) return null;

        return Build(under.Select(kv => (kv.Key[prefix.Length..], kv.Value)).ToArray());
    }

    public bool Set(string path, string json)
    {
        if (Refuse?.Invoke(path, json) == true) return false;
        Clear(path);
        Expand(path, json);
        Writes.Add((path, json));
        Notify(path);
        return true;
    }

    public bool Update(string path, string json)
    {
        if (Refuse?.Invoke(path, json) == true) return false;

        using var doc = JsonDocument.Parse(json);
        foreach (var member in doc.RootElement.EnumerateObject())
        {
            var child = $"{path}/{member.Name}";
            Clear(child);
            Expand(child, member.Value.GetRawText());
        }
        Writes.Add((path, json));
        Notify(path);
        return true;
    }

    public bool Remove(string path)
    {
        if (Refuse?.Invoke(path, null) == true) return false;
        Clear(path);
        Writes.Add((path, null));
        Notify(path);
        return true;
    }

    public IDisposable Watch(string path, Action<string?> onValue)
    {
        var sub = new Subscription(this, path, onValue);
        _watches.Add(sub);
        // "On every change INCLUDING the first" — a subscriber's first callback is the current value,
        // which is what lets a client resume a game by subscribing to it.
        onValue(Read(path));
        return sub;
    }

    // A change at `path` is a change for anyone watching it, anyone watching an ancestor of it, and
    // anyone watching a descendant (a subtree replacement changes what is under it).
    private void Notify(string path)
    {
        foreach (var sub in _watches.ToArray())
        {
            if (sub.Path == path
                || path.StartsWith(sub.Path + "/", StringComparison.Ordinal)
                || sub.Path.StartsWith(path + "/", StringComparison.Ordinal))
            {
                sub.Deliver(Read(sub.Path));
            }
        }
    }

    private void Clear(string path)
    {
        var prefix = path + "/";
        foreach (var key in _leaves.Keys
                     .Where(k => k == path || k.StartsWith(prefix, StringComparison.Ordinal))
                     .ToArray())
        {
            _leaves.Remove(key);
        }
    }

    // Writing an object writes its leaves; writing null writes nothing, which is how a null deletes.
    private void Expand(string path, string json)
    {
        using var doc = JsonDocument.Parse(json);
        Expand(path, doc.RootElement);
    }

    private void Expand(string path, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in value.EnumerateObject())
                    Expand($"{path}/{member.Name}", member.Value);
                break;
            case JsonValueKind.Null:
                break;
            default:
                _leaves[path] = value.GetRawText();
                break;
        }
    }

    // Rebuild an object from the relative leaf paths under it, one level at a time.
    private static string Build((string Relative, string Value)[] leaves)
    {
        var sb = new StringBuilder("{");
        var first = true;

        foreach (var group in leaves.GroupBy(l => l.Relative.Split('/')[0]))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(JsonEncodedText.Encode(group.Key)).Append("\":");

            var direct = group.FirstOrDefault(l => l.Relative == group.Key);
            if (direct.Relative is not null)
            {
                sb.Append(direct.Value);
                continue;
            }

            sb.Append(Build(group
                .Select(l => (l.Relative[(group.Key.Length + 1)..], l.Value))
                .ToArray()));
        }

        return sb.Append('}').ToString();
    }

    private sealed class Subscription(FakeCloudStore store, string path, Action<string?> onValue) : IDisposable
    {
        private bool _live = true;

        public string Path => path;

        public void Deliver(string? value)
        {
            if (_live) onValue(value);
        }

        public void Dispose()
        {
            _live = false;
            store._watches.Remove(this);
        }
    }
}
