using System.Collections.Immutable;
using Chess.Lib;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// Formats a single move (white + black ply) as a row in the history panel.
/// </summary>
internal readonly record struct HistoryMoveRow(
    ImmutableList<RecordedPly> Plies,
    int MoveIndex,
    int? HighlightPlyIndex) : IRowFormatter
{
    public string FormatRow(int width)
    {
        var plyIdx = MoveIndex * 2;
        var (idxStr, whitePly) = Plies.GetRecordAndPGNIdx(plyIdx);
        var blackPlyStr = plyIdx + 1 < Plies.Count ? Plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

        var isHighlightedWhite = HighlightPlyIndex == plyIdx;
        var isHighlightedBlack = HighlightPlyIndex == plyIdx + 1;

        if (isHighlightedWhite || isHighlightedBlack)
        {
            var prefix = $" {idxStr} ";
            var whiteText = $"{whitePly,-8}";
            var blackText = $" {blackPlyStr,-8}";
            var remaining = width - prefix.Length - whiteText.Length - blackText.Length;

            var whiteColor = isHighlightedWhite ? "\e[97;44m" : "\e[37;40m";
            var blackColor = isHighlightedBlack ? "\e[97;44m" : "\e[37;40m";

            return $"\e[37;40m{prefix}{whiteColor}{whiteText}\e[37;40m{blackColor}{blackText}\e[37;40m{new string(' ', Math.Max(0, remaining))}\e[0m";
        }
        else
        {
            var line = $" {idxStr} {whitePly,-8} {blackPlyStr,-8}";
            return $"\e[37;40m{line.PadRight(width)}\e[0m";
        }
    }
}
