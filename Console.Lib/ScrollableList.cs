namespace Console.Lib;

/// <summary>
/// Multi-row scrollable list with a header row.
/// Each item implements <see cref="IRowFormatter"/> for its own row styling.
/// </summary>
public class ScrollableList<TItem>(ITerminalViewport viewport) : Widget(viewport) where TItem : IRowFormatter
{
    private IReadOnlyList<TItem> _items = [];
    private int _scrollOffset;
    private string _header = "";
    private string _headerStyle = "\e[97;100m";
    private string _emptyStyle = "\e[37;40m";

    /// <summary>Number of data rows visible (excluding header).</summary>
    public int VisibleRows => Math.Max(0, Viewport.Size.Height - (_header.Length > 0 ? 1 : 0));

    public ScrollableList<TItem> Items(IReadOnlyList<TItem> items) { _items = items; return this; }
    public ScrollableList<TItem> ScrollTo(int offset) { _scrollOffset = offset; return this; }
    public ScrollableList<TItem> Header(string text) { _header = text; return this; }
    public ScrollableList<TItem> HeaderStyle(string vtStyle) { _headerStyle = vtStyle; return this; }
    public ScrollableList<TItem> EmptyStyle(string vtStyle) { _emptyStyle = vtStyle; return this; }

    public override void Render()
    {
        var (width, height) = Viewport.Size;
        if (width <= 0 || height <= 0) return;

        var row = 0;
        if (_header.Length > 0)
        {
            if (!TrySetCursorPosition(Viewport, 0, row)) return;
            Viewport.Write($"{_headerStyle}{_header.PadRight(width)}\e[0m");
            row++;
        }

        for (; row < height; row++)
        {
            if (!TrySetCursorPosition(Viewport, 0, row)) return;

            var itemIdx = _scrollOffset + row - (_header.Length > 0 ? 1 : 0);
            if (itemIdx >= 0 && itemIdx < _items.Count)
            {
                Viewport.Write(_items[itemIdx].FormatRow(width));
            }
            else
            {
                Viewport.Write($"{_emptyStyle}{new string(' ', width)}\e[0m");
            }
        }
    }
}
