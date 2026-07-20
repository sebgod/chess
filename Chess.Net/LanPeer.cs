using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Chess.Net;

/// <summary>
/// A peer currently visible on the LAN, learned from its UDP announce beacon. <see cref="EndPoint"/>
/// is the peer's TCP session endpoint (its sender IP + the port it announced) — dial it to invite.
/// <see cref="LastSeen"/> drives lobby expiry: a peer that stops beaconing is dropped.
/// <see cref="MachineName"/>/<see cref="Pid"/> are only for disambiguating look-alike beacons in the
/// lobby list (see <see cref="ResolveLabels"/>).
/// </summary>
public sealed record LanPeer(string PeerId, string Name, string MachineName, int Pid, IPEndPoint EndPoint, DateTimeOffset LastSeen)
{
    /// <summary>The raw label — the typed name, or "Player" when unnamed. Progressive disambiguation
    /// (machine name, then a PID-ordered index) is layered on over a whole list by
    /// <see cref="ResolveLabels"/>; a lone name is shown as-is.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Player" : Name;

    /// <summary>
    /// Lobby labels for a set of peers, disambiguated only as far as needed so a unique name stays
    /// clean: "Seb" when the name is unique; "Seb (lap1)" when the name collides across machines;
    /// "Seb (lap1) #2" when it still collides on one machine (two instances), numbered by ascending
    /// PID so the ordering is stable regardless of arrival order. Returns one label per input peer,
    /// in the same order (so a caller can zip labels back to its peer list by index).
    /// </summary>
    public static string[] ResolveLabels(IReadOnlyList<LanPeer> peers)
    {
        var labels = new string[peers.Count];

        // Group indices by base name — a name seen only once needs no suffix at all.
        foreach (var byName in Enumerable.Range(0, peers.Count)
                     .GroupBy(i => peers[i].DisplayName, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.ToArray()))
        {
            if (byName.Length == 1)
            {
                labels[byName[0]] = peers[byName[0]].DisplayName;
                continue;
            }

            // Name collides: bring in the machine name (when known) to tell the machines apart.
            foreach (var byMachine in byName
                         .GroupBy(i => peers[i].MachineName ?? "", StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.ToArray()))
            {
                var machine = peers[byMachine[0]].MachineName ?? "";
                var stem = string.IsNullOrEmpty(machine)
                    ? peers[byMachine[0]].DisplayName
                    : $"{peers[byMachine[0]].DisplayName} ({machine})";

                if (byMachine.Length == 1)
                {
                    labels[byMachine[0]] = stem;
                    continue;
                }

                // Still colliding on a single machine (multiple instances): number by ascending PID.
                var ordered = byMachine.OrderBy(i => peers[i].Pid).ToArray();
                for (var k = 0; k < ordered.Length; k++)
                    labels[ordered[k]] = $"{stem} #{k + 1}";
            }
        }

        return labels;
    }
}
