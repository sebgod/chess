using Console.Lib;

namespace Chess.Tests;

internal sealed class FakeTerminal : IVirtualTerminal
{
    private readonly Queue<ConsoleInputEvent> _inputs;
    private int _width, _height;

    public FakeTerminal(Queue<ConsoleInputEvent> inputs, int width = 80, int height = 24)
    {
        _inputs = inputs;
        _width = width;
        _height = height;
    }

    public bool IsAlternateScreen { get; private set; }
    public (int Width, int Height) Size => (_width, _height);
    public (int Left, int Top)? LastCursorPosition { get; private set; }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Clear() { }
    public void SetCursorPosition(int left, int top) => LastCursorPosition = (left, top);
    public void Write(string text) { }
    public void WriteLine(string? text = null) { }
    public ConsoleColor ForegroundColor { get; set; }
    public ConsoleColor BackgroundColor { get; set; }
    public void ResetColor() { }
    public void Flush() { }
    public Stream OutputStream { get; } = Stream.Null;
    public bool HasInput() => _inputs.Count > 0;
    public ConsoleInputEvent TryReadInput() => _inputs.Dequeue();
    public Task InitAsync() => Task.CompletedTask;
    public bool HasSixelSupport => false;
    public (uint Width, uint Height) CellSize => (10, 20);

    public ValueTask EnterAlternateScreenAsync()
    {
        IsAlternateScreen = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
