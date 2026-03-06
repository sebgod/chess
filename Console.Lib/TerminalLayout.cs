namespace Console.Lib;

public sealed class TerminalLayout
{
    private readonly ITerminalViewport _root;
    private readonly List<(Dock Dock, int Size, TerminalViewport Viewport)> _edgeDocked = [];
    private TerminalViewport? _fillViewport;
    private int _lastWidth, _lastHeight;

    public TerminalLayout(ITerminalViewport root)
    {
        _root = root;
        var (w, h) = root.Size;
        _lastWidth = w;
        _lastHeight = h;
    }

    public TerminalViewport Dock(Dock dock, int size = 0)
    {
        var viewport = new TerminalViewport(_root, 0, 0, 0, 0);
        if (dock == Console.Lib.Dock.Fill)
            _fillViewport = viewport;
        else
            _edgeDocked.Add((dock, size, viewport));
        ComputeGeometries();
        return viewport;
    }

    public bool Recompute()
    {
        var (w, h) = _root.Size;
        if (w == _lastWidth && h == _lastHeight)
            return false;

        _lastWidth = w;
        _lastHeight = h;
        ComputeGeometries();
        return true;
    }

    private void ComputeGeometries()
    {
        var rx = 0;
        var ry = 0;
        var rw = _lastWidth;
        var rh = _lastHeight;

        foreach (var (dock, size, viewport) in _edgeDocked)
        {
            switch (dock)
            {
                case Console.Lib.Dock.Top:
                {
                    var panelHeight = Math.Min(size, rh);
                    viewport.UpdateGeometry(rx, ry, rw, panelHeight);
                    ry += panelHeight;
                    rh -= panelHeight;
                    break;
                }
                case Console.Lib.Dock.Bottom:
                {
                    var panelHeight = Math.Min(size, rh);
                    viewport.UpdateGeometry(rx, ry + rh - panelHeight, rw, panelHeight);
                    rh -= panelHeight;
                    break;
                }
                case Console.Lib.Dock.Left:
                {
                    var panelWidth = Math.Min(size, rw);
                    viewport.UpdateGeometry(rx, ry, panelWidth, rh);
                    rx += panelWidth;
                    rw -= panelWidth;
                    break;
                }
                case Console.Lib.Dock.Right:
                {
                    var panelWidth = Math.Min(size, rw);
                    viewport.UpdateGeometry(rx + rw - panelWidth, ry, panelWidth, rh);
                    rw -= panelWidth;
                    break;
                }
            }
        }

        _fillViewport?.UpdateGeometry(rx, ry, rw, rh);
    }
}
