using System.Collections.Concurrent;
using System.Diagnostics;

namespace Chess.UCI;

/// <summary>
/// GUI-side helper that manages communication with a UCI engine process.
/// </summary>
public sealed class UciClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<UciResponse> _responses = new();
    private readonly TaskCompletionSource _exited = new();
    private Task? _readTask;
    private bool _disposed;

    public UciClient(string enginePath)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = enginePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _process.Exited += (_, _) => _exited.TrySetResult();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _process.Start();

        _readTask = Task.Run(() => ReadOutputLoop(), ct);

        Send(new UciCommand.UciInit());
        await WaitForResponseAsync<UciResponse.UciOk>(ct);
    }

    public async Task WaitForReadyAsync(CancellationToken ct = default)
    {
        Send(new UciCommand.IsReady());
        await WaitForResponseAsync<UciResponse.ReadyOk>(ct);
    }

    public async Task NewGameAsync(CancellationToken ct = default)
    {
        Send(new UciCommand.UciNewGame());
        await WaitForReadyAsync(ct);
    }

    public async Task<UciResponse.BestMove> GoAsync(UciCommand.SetPosition position, UciCommand.Go goParams, CancellationToken ct = default)
    {
        Send(position);
        Send(goParams);
        return await WaitForResponseAsync<UciResponse.BestMove>(ct);
    }

    public void SendStop() => Send(new UciCommand.Stop());

    public bool TryGetResponse<T>(out T? response) where T : UciResponse
    {
        while (_responses.TryDequeue(out var r))
        {
            if (r is T typed)
            {
                response = typed;
                return true;
            }
        }
        response = default;
        return false;
    }

    private void Send(UciCommand command)
    {
        var line = UciFormatter.Format(command);
        _process.StandardInput.WriteLine(line);
        _process.StandardInput.Flush();
    }

    private async Task<T> WaitForResponseAsync<T>(CancellationToken ct) where T : UciResponse
    {
        while (!ct.IsCancellationRequested)
        {
            if (_responses.TryDequeue(out var response) && response is T typed)
            {
                return typed;
            }

            await Task.Delay(1, ct);
        }

        throw new OperationCanceledException(ct);
    }

    private void ReadOutputLoop()
    {
        try
        {
            while (_process.StandardOutput.ReadLine() is { } line)
            {
                if (UciParser.ParseResponse(line) is { } response)
                {
                    _responses.Enqueue(response);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Process was disposed
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                Send(new UciCommand.Quit());
                var exited = _exited.Task;
                if (await Task.WhenAny(exited, Task.Delay(3000)) != exited && !_process.HasExited)
                {
                    _process.Kill();
                }
            }
        }
        catch
        {
            // Best effort cleanup
        }

        if (_readTask is not null)
        {
            try { await _readTask; } catch { /* read loop may throw on cancellation */ }
        }

        _process.Dispose();
    }
}
