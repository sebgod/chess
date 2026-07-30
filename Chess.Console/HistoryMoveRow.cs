using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

namespace Chess.Console;

/// <summary>
/// Adapts the shared <see cref="HistoryRowLayout"/> row onto Console.Lib's <see cref="IRowLayout"/>, so a
/// <c>ScrollableList</c> row and <see cref="PixelGameDisplay{TSurface}"/>'s history panel are the same tree.
/// <para>
/// All that is local to the terminal is the palette: the SGR-16 set, so the row still reads on a
/// 16-colour terminal, plus a row background — cells have no filled panel behind them the way a pixel
/// surface does. The structure (which cell claims which ply, the spacer for a move with no reply yet)
/// lives in <see cref="HistoryRowLayout"/>; the row states no absolute extent, which is what lets one
/// tree measure in cells here and in pixels there.
/// </para>
/// </summary>
internal readonly record struct HistoryMoveRow(
    ImmutableList<RecordedPly> Plies,
    int MoveIndex,
    int? HighlightPlyIndex) : IRowLayout
{
    private static readonly HistoryRowPalette Palette = new(
        Index: SgrColor.White.ToRgba(),
        Ply: SgrColor.White.ToRgba(),
        Highlight: SgrColor.BrightWhite.ToRgba(),
        HighlightBackground: SgrColor.Blue.ToRgba(),
        Background: SgrColor.Black.ToRgba());

    /// <summary>One design unit is one cell here, so a font size of 1 is one row of text.</summary>
    private const float CellFontSize = 1f;

    public Layout.Node BuildRow(in RowContext context) =>
        HistoryRowLayout.BuildRow(Plies, MoveIndex, HighlightPlyIndex, CellFontSize, Palette);
}
