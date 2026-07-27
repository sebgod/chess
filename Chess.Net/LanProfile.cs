using System.IO;

namespace Chess.Net;

/// <summary>
/// The local player's LAN profile: just the persisted display name, stored as one line beside the game
/// save (<c>LocalApplicationData/SharpAstro.Chess</c> on desktop, <c>FilesDir</c> on Android) — ask for
/// it once, prefill and let the user edit it thereafter. It becomes the beacon's node name, so it is
/// what other players pick from in the lobby.
///
/// <para>The peer id that used to live here is <c>LAN.Lib.LanIdentity</c>'s job now: minted fresh per
/// process, never persisted, and used only for discovery's self-echo filter. (Persisting it was
/// exactly what made two instances on one machine — sharing one lan.txt — load the same id and then
/// silently ignore each other as their own echo.)</para>
/// </summary>
public sealed record LanProfile(string Name)
{
    public const string FileName = "lan.txt";

    /// <summary>Load the saved name from <paramref name="directory"/> (empty if none).</summary>
    public static LanProfile Load(string directory)
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
            // fall through to an empty name
        }

        return new LanProfile(name);
    }

    /// <summary>Persist the name to <paramref name="directory"/>. Best-effort — a failed write is
    /// swallowed.</summary>
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
