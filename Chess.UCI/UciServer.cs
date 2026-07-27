namespace Chess.UCI;

/// <summary>
/// Engine-side helper that reads UCI commands from stdin and dispatches to an <see cref="IUciEngine"/>.
/// </summary>
public static class UciServer
{
    private static readonly Lock WriteLock = new();

    public static async Task RunAsync(IUciEngine engine, TextReader input, TextWriter output, CancellationToken ct)
    {
        // The search runs off the read loop so that "stop" — and "isready", which the spec says must
        // always be answered — can still be received while it is thinking. A search that blocked here
        // would make "stop" unreachable by construction.
        var search = Task.CompletedTask;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await input.ReadLineAsync(ct);
                if (line is null) break;

                var command = UciParser.ParseCommand(line);
                if (command is null) continue;

                switch (command)
                {
                    case UciCommand.UciInit:
                        engine.OnUci(output);
                        break;
                    case UciCommand.IsReady:
                        engine.OnIsReady(output);
                        break;
                    case UciCommand.UciNewGame:
                        // Mutates engine state, so it must not overlap a running search. A well-behaved
                        // GUI sends "stop" first; if it did not, this waits for the search to finish.
                        await search;
                        engine.OnNewGame();
                        break;
                    case UciCommand.SetPosition pos:
                        await search;
                        engine.OnPosition(pos.Fen, pos.Moves);
                        break;
                    case UciCommand.Go go:
                        await search;
                        search = Task.Run(() => engine.OnGo(go, output));
                        break;
                    case UciCommand.Stop:
                        engine.OnStop(output);
                        break;
                    case UciCommand.Quit:
                        engine.OnStop(output);
                        await search;
                        return;
                    case UciCommand.Debug dbg:
                        engine.OnDebug(dbg.On);
                        break;
                }
            }
        }
        finally
        {
            // Never leave a search running behind us: stdin closing (line is null) or cancellation
            // must still unwind the engine thread.
            engine.OnStop(output);
            await search.ConfigureAwait(false);
        }
    }

    public static void WriteResponse(TextWriter output, UciResponse response)
    {
        // "bestmove" and "info" now come from the search thread while the read loop may be answering
        // "readyok", so the write has to be atomic or the two interleave mid-line.
        lock (WriteLock)
        {
            output.WriteLine(UciFormatter.Format(response));
            output.Flush();
        }
    }
}
