using Console.Lib;

namespace Chess.Tests;

internal sealed class FakeTerminal(Queue<ConsoleInputEvent> inputs, int width = 80, int height = 24) : IVirtualTerminal
{
    public bool IsAlternateScreen { get; private set; }
    public (int Width, int Height) Size => (width, height);
    public void Clear() { }
    public void SetCursorPosition(int left, int top) { }
    public void Write(string text) { }
    public void WriteLine(string text = "") { }
    public ConsoleColor ForegroundColor { get; set; }
    public ConsoleColor BackgroundColor { get; set; }
    public void ResetColor() { }
    public void Flush() { }
    public Stream OutputStream { get; } = Stream.Null;
    public bool HasInput() => inputs.Count > 0;
    public ConsoleInputEvent TryReadInput() => inputs.Dequeue();
    public Task<bool> HasSixelSupportAsync() => Task.FromResult(false);
    public Task<(uint Width, uint Height)?> QueryCellSizeAsync() => Task.FromResult<(uint, uint)?>(null);

    public ValueTask EnterAlternateScreenAsync()
    {
        IsAlternateScreen = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
