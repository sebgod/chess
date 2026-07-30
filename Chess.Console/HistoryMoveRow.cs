using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
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
/// <para>
/// Each ply cell also CLAIMS its click, so the ply a hit resolves to comes off the same arranged rect
/// that painted it — the reason the tree form exists. The caller used to split the row at half the
/// content width, which put the boundary at column 11 while White's ply is painted through column 14,
/// so clicking the tail of a long white move jumped to Black's.
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
        var hasBlack = plyIdx + 1 < Plies.Count;

        return Layout.Builder.HStack(
                // The move number picks White's ply, which is what splitting the row by width happened to
                // do for these columns and is the reading a user expects -- the number labels the move, and
                // its first half is White's. It is not highlighted, so it stays a plain cell that hits.
                Layout.Builder.Text($" {idxStr} ", 1f, Text).HStar()
                    .Clickable(new HitResult.ListItemHit(GameUI.HistoryListId, plyIdx)),
                Ply(whitePly.ToString(), plyIdx, PlyColumns),
                // The leading space rides inside the black cell, so a highlighted black ply reads as one
                // continuous block -- the old string put that space inside blackStyle for the same reason.
                hasBlack
                    ? Ply($" {Plies.GetRecordAndPGNIdx(plyIdx + 1).Ply}", plyIdx + 1, PlyColumns + 1)
                    // No black ply yet: a spacer rather than an empty Text, so the half-row cannot claim a
                    // hit on a ply that does not exist. The guard used to live in the caller.
                    : Layout.Builder.Spacer().WFixed(PlyColumns + 1).HStar(),
                Layout.Builder.Spacer().WStar().HStar())
            .Bg(RowBg);
    }

    private Layout.Node Ply(string text, int plyIndex, int columns)
    {
        var highlighted = HighlightPlyIndex == plyIndex;
        var leaf = Layout.Builder.Text(text, 1f, highlighted ? HighlightText : Text)
            .WFixed(columns).HStar()
            .Clickable(new HitResult.ListItemHit(GameUI.HistoryListId, plyIndex));
        return highlighted ? leaf.Bg(HighlightBg) : leaf;
    }
}
