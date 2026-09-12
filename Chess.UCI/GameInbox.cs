using System;
using System.Collections.Generic;
using System.IO;
using Chess.Lib;
using File = System.IO.File;

namespace Chess.UCI;

/// <summary>
/// One game in the inbox: what <see cref="SavedGame"/> carries, plus the facts that are about
/// <em>this device's copy</em> rather than about the game — which file it is, and when it last
/// changed.
/// </summary>
public readonly record struct InboxEntry(string Id, SavedGame Saved, DateTimeOffset LastMove)
{
    public Game Game => Saved.Game;
    public GameMode Mode => Saved.Mode;

    /// <summary>The colour the OTHER player has: the engine's, or a correspondent's.</summary>
    public Side ComputerSide => Saved.ComputerSide;

    /// <summary>The other player's display name, or "" when there is nobody to name.</summary>
    public string Opponent => Saved.Opponent;

    /// <summary>
    /// The colour played at this device — the opposite of <see cref="ComputerSide"/>, and
    /// <see cref="Side.None"/> for hot-seat, which has no single local side because both players
    /// are here.
    /// </summary>
    public Side LocalSide => ComputerSide switch
    {
        Side.White => Side.Black,
        Side.Black => Side.White,
        _ => Side.None,
    };

    /// <summary>
    /// True when this game is waiting on the player at this device.
    ///
    /// <para>Correspondence only, deliberately. A hot-seat game is <em>always</em> "your move" and a
    /// game against the engine resolves itself within a second, so listing either in a view whose
    /// whole purpose is "these need you" would bury the ones that do.</para>
    /// </summary>
    public bool IsWaitingOnYou =>
        Mode is GameMode.PlayByLink && !Game.IsFinished && Game.CurrentSide == LocalSide;

    /// <summary>Whether this entry has sat untouched longer than <see cref="GameInbox.StaleAfter"/>.</summary>
    public bool IsStale(DateTimeOffset now) => now - LastMove > GameInbox.StaleAfter;

    /// <summary>
    /// Who this game is against — the opponent's name when there is one, otherwise what the mode
    /// makes them. A saved game always has a describable opponent, even if it is "nobody".
    /// </summary>
    public string OpponentLabel => !string.IsNullOrEmpty(Opponent) ? Opponent : Mode switch
    {
        GameMode.PlayByLink => "Link game",
        GameMode.PlayerVsComputer => "vs Computer",
        GameMode.NetworkGame => "LAN game",
        GameMode.AcrossTheTable => "Across the table",
        GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard => "Custom game",
        _ => "Hot seat",
    };

    /// <summary>
    /// The one-line description a picker shows. Canonical here rather than in each front-end, for the
    /// same reason <c>GameUI.StatusLine</c> is: three hosts will want this row and three hand-written
    /// versions would drift on exactly the details that matter — whose turn it is, and how long ago.
    /// </summary>
    public string Summary(DateTimeOffset now)
    {
        var moves = (Game.PlyCount + 1) / 2;
        var state = Game.IsFinished ? "finished"
            : IsWaitingOnYou ? "your move"
            : Mode is GameMode.PlayByLink ? "their move"
            : Game.CurrentSide is Side.Black ? "black to move" : "white to move";

        return $"{OpponentLabel} — {state} · {moves} {(moves == 1 ? "move" : "moves")} · {Ago(now)}";
    }

    /// <summary>How long ago this game last changed, in the coarsest unit that still says something.
    /// A correspondence game is measured in days, so hours are noise and seconds are a lie.</summary>
    private string Ago(DateTimeOffset now)
    {
        var days = (int)(now - LastMove).TotalDays;

        return days switch
        {
            < 0 => "just now", // a clock that moved backwards must not print "-3 days ago"
            0 => "today",
            1 => "yesterday",
            < 14 => $"{days} days ago",
            < 60 => $"{days / 7} weeks ago",
            _ => $"{days / 30} months ago",
        };
    }
}

/// <summary>
/// A directory of saved games — the "inbox" correspondence play needs, where
/// <see cref="GameStore"/> handles exactly one file.
///
/// <para>The split is the point: a single game's format, replay and validation are
/// <see cref="GameStore"/>'s and are shared by every front-end; which games exist, which are waiting
/// on you and which have gone quiet are this type's, and only a front-end that offers more than one
/// game at a time needs them. Chess.Droid keeps using the single-file API unchanged.</para>
///
/// <para>Why a directory of small files rather than one index file: two of them can never disagree.
/// An index has to be rewritten on every move and is the thing that goes stale or half-written when
/// a process dies mid-save, and the recovery story for a corrupt index is worse than for a corrupt
/// game — it loses every game rather than one.</para>
/// </summary>
public static class GameInbox
{
    /// <summary>Subdirectory, under the host's data folder, holding one file per game.</summary>
    public const string FolderName = "games";

    /// <summary>
    /// How long a correspondence game may sit untouched before the inbox stops listing it by default.
    ///
    /// <para>It is <b>hidden, never deleted</b>. A game with no move for four months is almost
    /// certainly over, and an inbox that lists it forever is an inbox nobody reads — but it is still
    /// a valid game, and somebody who went away mid-game should get it back. Deleting on a timer is
    /// the one version of this that is hard to forgive, because the premise of correspondence play is
    /// that a game may legitimately take months.</para>
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(120);

    /// <summary>The folder <paramref name="directory"/>'s games live in.</summary>
    public static string FolderFor(string directory) => Path.Combine(directory, FolderName);

    /// <summary>
    /// Every game in the inbox, most recently changed first. An unreadable or incompatible entry is
    /// skipped rather than failing the read: one corrupt file must not cost the player every other
    /// game. Empty when the folder does not exist yet.
    /// </summary>
    public static IReadOnlyList<InboxEntry> Load(string directory, Action<string>? log = null)
    {
        var folder = FolderFor(directory);
        if (!Directory.Exists(folder)) return [];

        var entries = new List<InboxEntry>();

        try
        {
            foreach (var file in Directory.GetFiles(folder, "*.uci"))
            {
                if (TryLoadFile(file, log) is { } entry) entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[inbox] listing failed: {ex.GetType().Name}: {ex.Message}");
        }

        entries.Sort((a, b) => b.LastMove.CompareTo(a.LastMove));
        return entries;
    }

    /// <summary>One entry by id, or null when it is absent or unreadable.</summary>
    public static InboxEntry? TryLoad(string directory, string id, Action<string>? log = null) =>
        TryLoadFile(PathFor(directory, id), log);

    /// <summary>
    /// Writes a game into the inbox under <paramref name="id"/>, stamping <paramref name="now"/> as
    /// its last-changed time. Best-effort, exactly like <see cref="GameStore.Save"/>: a failed write
    /// must never take down the game it was trying to preserve.
    /// </summary>
    public static void Save(
        string directory, string id, Game game, Side computerSide, GameMode mode,
        string opponent, DateTimeOffset now, Action<string>? log = null)
    {
        try
        {
            Directory.CreateDirectory(FolderFor(directory));
            GameStore.Save(PathFor(directory, id), game, computerSide, mode, log,
                opponent: opponent, lastMove: now);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[inbox] write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Removes a game. Best-effort; a missing file is not an error.</summary>
    public static void Delete(string directory, string id, Action<string>? log = null)
    {
        try { File.Delete(PathFor(directory, id)); }
        catch (Exception ex) { log?.Invoke($"[inbox] delete failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// A fresh id: time-ordered, filename-safe, and collision-free without a registry to consult.
    /// Time-ordered so a bare directory listing is already chronological, which makes the files
    /// legible to a human with nothing but a file manager.
    /// </summary>
    public static string NewId(DateTimeOffset now) =>
        now.ToUnixTimeMilliseconds().ToString("x8") + "-" + Guid.NewGuid().ToString("N")[..6];

    /// <summary>
    /// Moves a pre-inbox <c>game.uci</c> into the inbox, once. Idempotent and best-effort.
    ///
    /// <para>An explicit call rather than magic inside <see cref="Load"/>, because a read that
    /// quietly rewrites the disk is the kind of behaviour nobody expects the first time it matters,
    /// and the host should decide when the one-time move happens.</para>
    /// </summary>
    public static void MigrateLegacySave(string directory, DateTimeOffset now, Action<string>? log = null)
    {
        var legacy = Path.Combine(directory, "game.uci");

        try
        {
            if (!File.Exists(legacy)) return;

            if (GameStore.TryLoad(legacy, log) is not { } saved)
            {
                // Unreadable: deleting is still right. Nothing can ever resume it, and leaving it
                // means retrying this on every launch forever.
                File.Delete(legacy);
                log?.Invoke("[inbox] discarded an unreadable legacy save");
                return;
            }

            // The legacy file's mtime is the only record of when it was last played, so it carries
            // over as the entry's timestamp rather than stamping everything "now" and making an
            // abandoned game look fresh.
            var played = new DateTimeOffset(File.GetLastWriteTimeUtc(legacy), TimeSpan.Zero);
            Save(directory, NewId(now), saved.Game, saved.ComputerSide, saved.Mode, saved.Opponent,
                played, log);
            File.Delete(legacy);
            log?.Invoke("[inbox] migrated the legacy save");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[inbox] legacy migration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string PathFor(string directory, string id) =>
        Path.Combine(FolderFor(directory), id + ".uci");

    private static InboxEntry? TryLoadFile(string path, Action<string>? log)
    {
        if (GameStore.TryLoad(path, log) is not { } saved) return null;

        // A save written before the `at=` token existed has no stored time. The file's mtime is the
        // fallback and only the fallback: it is reset by copying, syncing or restoring from backup,
        // which is exactly why the timestamp is stored in the file for everything written since.
        var lastMove = saved.LastMove
            ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

        return new InboxEntry(Path.GetFileNameWithoutExtension(path), saved, lastMove);
    }
}
