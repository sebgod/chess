using System;
using System.IO;

namespace Chess.Net;

/// <summary>
/// The local player's LAN identity: a display name plus a stable random id that survives restarts, so
/// a peer is recognisably "the same device" across sessions without any login. Persisted as two lines
/// (name, id) beside the game save (<c>LocalApplicationData/SharpAstro.Chess</c> on desktop,
/// <c>FilesDir</c> on Android). Ask for the name once; prefill and let the user edit it thereafter.
/// </summary>
public sealed record LanIdentity(string Name, string PeerId)
{
    public const string FileName = "lan.txt";

    /// <summary>Load the identity from <paramref name="directory"/>, minting a fresh id (empty name)
    /// if none is stored or the file is unreadable.</summary>
    public static LanIdentity Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                var name = lines.Length > 0 ? lines[0].Trim() : "";
                var id = lines.Length > 1 ? lines[1].Trim() : "";
                if (!string.IsNullOrEmpty(id))
                    return new LanIdentity(name, id);
            }
        }
        catch
        {
            // fall through to a fresh identity
        }

        return new LanIdentity("", Guid.NewGuid().ToString("N"));
    }

    /// <summary>Persist to <paramref name="directory"/>. Best-effort — a failed write is swallowed.</summary>
    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FileName), $"{Name}\n{PeerId}");
        }
        catch
        {
            // best-effort
        }
    }
}
