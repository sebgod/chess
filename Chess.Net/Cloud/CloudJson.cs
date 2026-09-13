using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Chess.Net.Cloud;

/// <summary>
/// The little JSON this assembly needs, without a serializer.
///
/// <para><c>Chess.Net</c> is <c>IsAotCompatible</c>, so the reflection-based
/// <c>JsonSerializer.Serialize&lt;T&gt;</c> is out. The design notes offered two ways round that — a
/// source-generated <c>JsonSerializerContext</c>, or hand-rolling the parser — and both are heavier
/// than the job needs. There is a third: <c>JsonDocument</c> and <c>JsonEncodedText</c> are DOM and
/// escaping APIs that reflect over nothing, ship in the shared framework (so still no package), and
/// are trim-safe. What is left is naming the four or five fields we actually read, which is what this
/// file is.</para>
///
/// <para>Every reader here answers with a fallback rather than throwing. The rows come off a network
/// and are written by other clients, so a missing or mistyped field is data to survive, not an
/// exception to propagate into a render loop.</para>
/// </summary>
internal static class CloudJson
{
    /// <summary>One JSON string literal, quotes and escaping included.</summary>
    public static string Quote(string value) => $"\"{JsonEncodedText.Encode(value)}\"";

    /// <summary>Parse, or null if it isn't JSON at all. The caller owns the document.</summary>
    public static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>A string member, or <paramref name="fallback"/> when absent or not a string.</summary>
    public static string StringOr(JsonElement obj, string name, string fallback = "") =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    /// <summary>An integer member, or <paramref name="fallback"/> when absent or not a number.</summary>
    public static int IntOr(JsonElement obj, string name, int fallback = 0) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var i)
            ? i
            : fallback;

    /// <summary>A long member (timestamps are milliseconds since the epoch, which overflows int in
    /// 1970 + 25 days), or <paramref name="fallback"/>.</summary>
    public static long LongOr(JsonElement obj, string name, long fallback = 0) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var l)
            ? l
            : fallback;

    /// <summary>True when the member is present and <c>true</c>.</summary>
    public static bool IsTrue(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.True;

    /// <summary>A nested object, or a default element (<c>ValueKind == Undefined</c>) when absent —
    /// which every reader above then treats as "no members", so a caller needs no null check.</summary>
    public static JsonElement Child(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) ? v : default;

    /// <summary>
    /// Merge <paramref name="patch"/>'s top-level members into <paramref name="cached"/>, a null
    /// member meaning "remove". Returns null when either side is not an object, which tells the
    /// caller to go and read the path instead of guessing.
    ///
    /// <para>Top-level is all a server-sent <c>patch</c> event at <c>/</c> ever carries for the rows
    /// this courier uses — <c>{g, n}</c> on a game, one row on the lobby — so a general JSON merge
    /// would be code written for a case that cannot arrive, with the fallback already in place for
    /// the one that could.</para>
    /// </summary>
    public static string? Merge(string? cached, string patch)
    {
        using var patchDoc = TryParse(patch);
        if (patchDoc is null || patchDoc.RootElement.ValueKind != JsonValueKind.Object) return null;

        using var cachedDoc = TryParse(cached);
        var have = cachedDoc?.RootElement ?? default;
        if (cached is not null && have.ValueKind != JsonValueKind.Object && have.ValueKind != JsonValueKind.Undefined)
            return null;

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            if (have.ValueKind == JsonValueKind.Object)
            {
                foreach (var member in have.EnumerateObject())
                {
                    if (patchDoc.RootElement.TryGetProperty(member.Name, out _)) continue; // superseded
                    member.WriteTo(w);
                }
            }
            foreach (var member in patchDoc.RootElement.EnumerateObject())
            {
                if (member.Value.ValueKind == JsonValueKind.Null) continue; // a null member is a delete
                member.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Set (or, with a null <paramref name="valueJson"/>, remove) one top-level member. Same contract
    /// as <see cref="Merge"/>: null means "this shape isn't one we can fold in, go and read it".
    /// </summary>
    public static string? SetMember(string? cached, string name, string valueJson)
    {
        using var valueDoc = TryParse(valueJson);
        var remove = valueDoc is null || valueDoc.RootElement.ValueKind == JsonValueKind.Null;

        using var cachedDoc = TryParse(cached);
        var have = cachedDoc?.RootElement ?? default;
        if (cached is not null && have.ValueKind != JsonValueKind.Object && have.ValueKind != JsonValueKind.Undefined)
            return null;

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            if (have.ValueKind == JsonValueKind.Object)
            {
                foreach (var member in have.EnumerateObject())
                {
                    if (string.Equals(member.Name, name, StringComparison.Ordinal)) continue;
                    member.WriteTo(w);
                }
            }
            if (!remove)
            {
                w.WritePropertyName(name);
                valueDoc!.RootElement.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
