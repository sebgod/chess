using System.Collections.Immutable;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

namespace Chess.Lib.UI;

/// <summary>
/// The colours one move-history row paints with. Every front-end keeps its own palette — the terminal's
/// comes from the SGR-16 set so it survives a 16-colour terminal, the pixel displays' from their dark
/// chrome theme — but the row STRUCTURE is shared (see <see cref="HistoryRowLayout"/>).
/// </summary>
/// <param name="Index">The move number's colour.</param>
/// <param name="Ply">A ply's colour when it is not the playback cursor.</param>
/// <param name="Highlight">The playback cursor ply's text colour.</param>
/// <param name="HighlightBackground">The playback cursor ply's cell fill — it is per PLY, not per row.</param>
/// <param name="Background">Fill for the whole row, or null to let the panel's own background show through.
/// The terminal needs it (cells have no panel fill behind them); the pixel panels already fill.</param>
public readonly record struct HistoryRowPalette(
    RGBAColor32 Index,
    RGBAColor32 Ply,
    RGBAColor32 Highlight,
    RGBAColor32 HighlightBackground,
    RGBAColor32? Background = null);

/// <summary>
/// One move (White's ply + Black's reply) as a <see cref="Layout"/> row, shared by every front-end that
/// shows a move history: the terminal's <c>ScrollableList</c> rows and <see cref="PixelGameDisplay{TSurface}"/>'s
/// history panel build the same tree.
///
/// <para>What is shared is the part that must not drift: which cell claims which ply. Each ply cell states its
/// own background and CLAIMS its own click, so the ply a hit resolves to comes off the same arranged rect that
/// painted it — draw == hit by construction. The move number labels the move, so it hits White's ply; a move
/// with no reply yet gets a <em>spacer</em> rather than an empty text cell, so the empty half cannot claim a
/// hit on a ply that does not exist. Both used to be re-derived per front-end, and the terminal's copy split
/// the row at half its width while White's ply painted past that column — clicking the tail of a long white
/// move jumped to Black's reply.</para>
///
/// <para><b>The row states no absolute extent, which is what lets one tree serve a cell surface and a pixel
/// surface.</b> The index cell is <c>Auto</c> — as wide as its own text — and the two ply cells are
/// <c>Star</c>, so they split whatever is left. That works on both surfaces because the PGN index is
/// space-padded to a constant character count (<see cref="RecordedPlyExtensions.GetRecordAndPGNIdx"/>'s
/// <c>{0,4}</c>) and DejaVu Sans advances a space all but exactly like a digit — 1298 against 1303 units of a
/// 2048 em, a thirty-second of a pixel at a 13px chrome font — so the index column is constant-width whether
/// it measures in cells or in pixels, and the ply columns below it stay aligned. That is a property of the
/// font, so it is pinned by a test (<c>HistoryRowLayoutTests</c>).
/// A row that DID need an absolute extent (a fixed column, a gap in cells) would have to declare the tree's
/// unit convention instead, via <c>CellMeasureContext.PixelAuthored</c> or DIR.Lib 7.4's mirror
/// <c>PixelMeasureContext.CellAuthored</c>; this row needs neither.</para>
/// </summary>
public static class HistoryRowLayout
{
    /// <summary>
    /// Builds move <paramref name="moveIndex"/> (0-based: move 1 is index 0) as a row.
    /// <para>The returned node states no sizing of its own, so the caller sizes it for its own container:
    /// a <c>ScrollableList</c> arranges each row into its one-cell rect, while a pixel panel stacks rows
    /// and gives each a <c>.RowH(rowHeight)</c>.</para>
    /// </summary>
    /// <param name="plies">The game's recorded plies (both sides, interleaved).</param>
    /// <param name="moveIndex">Which move row to build.</param>
    /// <param name="highlightPlyIndex">The playback cursor's ply, or null when not in playback.</param>
    /// <param name="fontSize">Text size in the tree's design units — <c>1f</c> on a cell surface (one row
    /// of text), the chrome font size in pixels on a pixel surface.</param>
    /// <param name="palette">The front-end's colours.</param>
    public static Layout.Node BuildRow(
        ImmutableList<RecordedPly> plies,
        int moveIndex,
        int? highlightPlyIndex,
        float fontSize,
        in HistoryRowPalette palette)
    {
        var whitePlyIdx = moveIndex * 2;
        var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(whitePlyIdx);
        var hasBlack = whitePlyIdx + 1 < plies.Count;

        var row = Layout.Builder.HStack(
            // Auto width: the padded index text IS the column. The pad also right-aligns the number for
            // free ("   1." / "  12."), so this needs no Far alignment inside a fixed box to line up.
            Layout.Builder.Text($" {idxStr} ", fontSize, palette.Index).HStar()
                .Clickable(new HitResult.ListItemHit(GameUI.HistoryListId, whitePlyIdx)),
            PlyCell(whitePly.ToString(), whitePlyIdx, highlightPlyIndex, fontSize, palette),
            // The leading space rides INSIDE the black cell, so a highlighted reply reads as one continuous
            // block indented off White's column rather than a glyph flush against it.
            hasBlack
                ? PlyCell($" {plies.GetRecordAndPGNIdx(whitePlyIdx + 1).Ply}", whitePlyIdx + 1,
                    highlightPlyIndex, fontSize, palette)
                : Layout.Builder.Spacer().Stretch());

        return palette.Background is { } bg ? row.Bg(bg) : row;
    }

    /// <summary>One ply: a star-width cell that pads itself in its own colour, so the playback highlight
    /// fills its column exactly, and that claims the click for its own ply.</summary>
    private static Layout.Node PlyCell(string text, int plyIndex, int? highlightPlyIndex, float fontSize,
        in HistoryRowPalette palette)
    {
        var highlighted = highlightPlyIndex == plyIndex;
        var cell = Layout.Builder.Text(text, fontSize, highlighted ? palette.Highlight : palette.Ply)
            .Stretch()
            .Clickable(new HitResult.ListItemHit(GameUI.HistoryListId, plyIndex));
        return highlighted ? cell.Bg(palette.HighlightBackground) : cell;
    }
}
