namespace Console.Lib;

/// <summary>
/// Container that arranges widgets using dock-based layout.
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
    /// Creates a viewport docked to an edge and returns it for widget construction.
    /// </summary>
    public ITerminalViewport Dock(DockStyle dock, int size)
    {
        var vp = _layout.Dock(dock, size);
        return vp;
    }

    /// <summary>
    /// Creates a fill viewport and returns it for widget construction.
    /// </summary>
    public ITerminalViewport Fill()
    {
        var vp = _layout.Dock(DockStyle.Fill);
        return vp;
    }

    /// <summary>
    /// Registers a widget for rendering.
    /// </summary>
    public Panel Add(Widget widget)
    {
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
