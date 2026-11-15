using System;
using System.IO;

namespace Chess.Net.Cloud;

/// <summary>
/// Where a desktop head finds <c>firebase-config.json</c>, the backend config that turns on "Online game".
///
/// <para>Two places, first one wins. <b>Next to the app</b> (<see cref="AppContext.BaseDirectory"/>), for a
/// build that ships with the config. <b>The user's own copy</b> (<see cref="UserPath"/>,
/// <c>%APPDATA%\chess\firebase-config.json</c> on Windows, <c>~/.config/chess/firebase-config.json</c>
/// elsewhere), for a developer machine. The user's copy is outside every checkout, so a local build
/// on our machines gets online play while a fork, a clone or a PR build, which has no such file, does
/// not. The mobile builds bundle the same file at build time (<c>ChessCloudConfig</c> in
/// Directory.Build.props).</para>
///
/// <para>Absent, unreadable or not a JSON object all read as "no backend", which is a normal state: the
/// menu simply has no "Online game".</para>
/// </summary>
public static class CloudConfigFile
{
    public const string FileName = "firebase-config.json";

    /// <summary>The per-user copy: the same place on every machine of ours, in no repository.</summary>
    public static string UserPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "chess", FileName);

    /// <summary>The config next to the app, else the user's copy, else null.</summary>
    public static string? TryLoad() => TryLoad(Path.Combine(AppContext.BaseDirectory, FileName), UserPath);

    /// <summary>The first of <paramref name="candidates"/> that holds a JSON object, else null.</summary>
    public static string? TryLoad(params string[] candidates)
    {
        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var text = File.ReadAllText(path).Trim();
                if (text.StartsWith('{')) return text;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
}
