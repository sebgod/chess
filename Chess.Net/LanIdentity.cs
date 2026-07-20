using System;
using System.IO;

namespace Chess.Net;

/// <summary>
/// The local player's LAN identity: a persisted display name plus a <b>per-process</b> peer id. Only
/// the name is stored (one line) beside the game save (<c>LocalApplicationData/SharpAstro.Chess</c> on
/// desktop, <c>FilesDir</c> on Android) — ask for it once, prefill and let the user edit it thereafter.
///
/// <para>The peer id is minted fresh on every <see cref="Load"/> and never persisted. Its only job is
/// the discovery self-echo filter (<see cref="LanDiscovery"/>), which needs the id to be unique
/// <i>per running process</i>, not stable across sessions — nothing reconnects to a known id. Persisting
/// it was exactly what made two instances on one machine (sharing one lan.txt) load the same id and then
/// silently ignore each other as their own echo. A version-7 GUID is unique and time-ordered (so peers
/// naturally sort by join time).</para>
/// </summary>
public sealed record LanIdentity(string Name, string PeerId)
{
    public const string FileName = "lan.txt";

    /// <summary>Load the saved name from <paramref name="directory"/> (empty if none), always paired
    /// with a freshly minted per-process peer id.</summary>
    public static LanIdentity Load(string directory)
    {
        var name = "";
        try
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length > 0)
                    name = lines[0].Trim();
                // Any second line is a peer id written by an older build — deliberately ignored now.
            }
        }
        catch
        {
            // fall through to just a fresh identity
        }

        return new LanIdentity(name, Guid.CreateVersion7().ToString("N"));
    }

    /// <summary>Persist the name to <paramref name="directory"/> (the peer id is per-process, never
    /// written). Best-effort — a failed write is swallowed.</summary>
    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FileName), Name);
        }
        catch
        {
            // best-effort
        }
    }
}
