using Microsoft.Extensions.Logging;

namespace Chess.Lib.UI;

/// <summary>
/// An <see cref="ILogger"/> over a line sink, for the DIR.Lib APIs that report through one
/// (<see cref="DIR.Lib.BackgroundTaskTracker.ProcessCompletions"/>) in a front-end whose diagnostics are a plain
/// <c>Action&lt;string&gt;</c> (stderr on the desktop, <c>SdlEventLoop.DiagnosticLog</c> on a device).
/// </summary>
public sealed class ActionLogger(System.Action<string> sink) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var text = formatter(state, exception);
        sink(exception is null ? $"[{logLevel}] {text}" : $"[{logLevel}] {text}: {exception}");
    }
}
