using System.Collections.Immutable;
using Chess.Lib;
using Console.Lib;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

namespace Chess.Console;

/// <summary>
/// Builds a single move (white + black ply) as a row in the history panel.
/// <para>
/// The highlight is per PLY, not per row, which is what makes the tree form clearly better than the
/// string it replaced: each ply is a cell that states its own background, so there is no interleaving
/// of style escapes and no "remaining = width - prefix - white - black" arithmetic to keep in step with
/// the format strings. The cells pad themselves in their own colour, so the highlight fills its column
/// exactly.
/// </para>
/// </summary>
internal readonly record struct HistoryMoveRow(
    ImmutableList<RecordedPly> Plies,
    int MoveIndex,
    int? HighlightPlyIndex) : IRowLayout
{
    private static readonly RGBAColor32 Text = SgrColor.White.ToRgba();
    private static readonly RGBAColor32 RowBg = SgrColor.Black.ToRgba();
    private static readonly RGBAColor32 HighlightText = SgrColor.BrightWhite.ToRgba();
    private static readonly RGBAColor32 HighlightBg = SgrColor.Blue.ToRgba();

    /// <summary>Columns a ply occupies, matching the old <c>{ply,-8}</c> field width.</summary>
    private const int PlyColumns = 8;

    public Layout.Node BuildRow(in RowContext context)
    {
        var plyIdx = MoveIndex * 2;
        var (idxStr, whitePly) = Plies.GetRecordAndPGNIdx(plyIdx);
        var blackPlyStr = plyIdx + 1 < Plies.Count ? Plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

        return Layout.Builder.HStack(
                Layout.Builder.Text($" {idxStr} ", 1f, Text).HStar(),
                Ply(whitePly.ToString(), HighlightPlyIndex == plyIdx, PlyColumns),
                // The leading space rides inside the black cell, so a highlighted black ply reads as one
                // continuous block -- the old string put that space inside blackStyle for the same reason.
                Ply($" {blackPlyStr}", HighlightPlyIndex == plyIdx + 1, PlyColumns + 1),
                Layout.Builder.Spacer().WStar())
            .Bg(RowBg);
    }

    private static Layout.Node Ply(string text, bool highlighted, int columns)
    {
        var leaf = Layout.Builder.Text(text, 1f, highlighted ? HighlightText : Text)
            .WFixed(columns).HStar();
        return highlighted ? leaf.Bg(HighlightBg) : leaf;
    }
}
