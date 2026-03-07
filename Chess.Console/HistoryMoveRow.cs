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
    private static readonly VtStyle Normal = new(SgrColor.White, SgrColor.Black);
    private static readonly VtStyle Highlight = new(SgrColor.BrightWhite, SgrColor.Blue);

    public string FormatRow(int width, ColorMode colorMode)
    {
        var plyIdx = MoveIndex * 2;
        var (idxStr, whitePly) = Plies.GetRecordAndPGNIdx(plyIdx);
        var blackPlyStr = plyIdx + 1 < Plies.Count ? Plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

        var isHighlightedWhite = HighlightPlyIndex == plyIdx;
        var isHighlightedBlack = HighlightPlyIndex == plyIdx + 1;

        var normal = Normal.Apply(colorMode);
        if (isHighlightedWhite || isHighlightedBlack)
        {
            var prefix = $" {idxStr} ";
            var whiteText = $"{whitePly,-8}";
            var blackText = $" {blackPlyStr,-8}";
            var remaining = width - prefix.Length - whiteText.Length - blackText.Length;

            var whiteStyle = (isHighlightedWhite ? Highlight : Normal).Apply(colorMode);
            var blackStyle = (isHighlightedBlack ? Highlight : Normal).Apply(colorMode);

            return $"{normal}{prefix}{whiteStyle}{whiteText}{normal}{blackStyle}{blackText}{normal}{new string(' ', Math.Max(0, remaining))}{VtStyle.Reset}";
        }
        else
        {
            var line = $" {idxStr} {whitePly,-8} {blackPlyStr,-8}";
            return $"{normal}{line.PadRight(width)}{VtStyle.Reset}";
        }
    }
}
