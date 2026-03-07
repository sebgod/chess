namespace Console.Lib;

/// <summary>
/// Container that arranges widgets using dock-based layout.
/// Widgets are created first, configured via fluent API, then placed into the panel.
/// </summary>
public class Panel
{
    private readonly TerminalLayout _layout;
    private readonly List<Widget> _children = [];

    public Panel(IVirtualTerminal terminal)
    {
        _layout = new TerminalLayout(terminal);
    }

    /// <summary>
    /// Docks a widget to an edge of the panel.
    /// </summary>
    public Panel Dock(DockStyle dock, int size, Widget widget)
    {
        var vp = _layout.Dock(dock, size);
        widget.Viewport = vp;
        _children.Add(widget);
        return this;
    }

    /// <summary>
    /// Places a widget in the remaining fill area.
    /// </summary>
    public Panel Fill(Widget widget)
    {
        var vp = _layout.Dock(DockStyle.Fill);
        widget.Viewport = vp;
        _children.Add(widget);
        return this;
    }

    /// <summary>
    /// Renders all child widgets.
    /// </summary>
    public void RenderAll()
    {
        foreach (var widget in _children)
            widget.Render();
    }

    /// <summary>
    /// Recomputes layout after terminal resize. Returns <c>true</c> if the size changed.
    /// </summary>
    public bool Recompute() => _layout.Recompute();
}
