using System.Text;
using Console.Lib;

namespace Chess.Console.Tests;

/// <summary>
/// A fake terminal that queues input events and captures output, for driving the console pipeline
/// without a real console. Shared rather than nested: <see cref="AsciiGameIntegrationTests"/> drives
/// a whole game through it and <see cref="HumanPlayerMotionTests"/> drives single events, and a
/// second copy of a fake this size drifts from the first.
/// </summary>
internal sealed class TestableTerminal(Queue<ConsoleInputEvent> inputs) : IVirtualTerminal
{
    private readonly StringBuilder _output = new();

    public string Output => _output.ToString();
    public void ClearOutput() => _output.Clear();

    // ITerminalViewport
    public (int Column, int Row) Offset => (0, 0);
    public (int Width, int Height) Size => (80, 24);
    public TermCell CellSize => new(10, 20);
    public ColorMode ColorMode => ColorMode.Sgr16;
    public void SetCursorPosition(int left, int top) { }
    public void Write(string text) => _output.Append(text);
    public void WriteLine(string? text = null) { _output.Append(text); _output.Append('\n'); }
    public void Flush() { }
    public Stream OutputStream => Stream.Null;

    // IVirtualTerminal
    public Task InitAsync() => Task.CompletedTask;
    public ImageDisplayCapability ImageDisplayCapability => ImageDisplayCapability.NoColor;
    public bool HasSixelSupport => false;
    public bool HasColorSupport => false;
    public bool IsInputRedirected => true;
    public bool IsOutputRedirected => false;
    public void EnterAlternateScreen() { }
    public bool IsAlternateScreen => false;
    public void Clear() => _output.Clear();
    public bool HasInput() => inputs.Count > 0;
    public ConsoleInputEvent TryReadInput() => inputs.Dequeue();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
