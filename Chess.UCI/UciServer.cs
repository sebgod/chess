namespace Chess.UCI;

/// <summary>
/// Engine-side helper that reads UCI commands from stdin and dispatches to an <see cref="IUciEngine"/>.
/// </summary>
public static class UciServer
{
    public static async Task RunAsync(IUciEngine engine, TextReader input, TextWriter output, CancellationToken ct)
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
                    engine.OnNewGame();
                    break;
                case UciCommand.SetPosition pos:
                    engine.OnPosition(pos.Fen, pos.Moves);
                    break;
                case UciCommand.Go go:
                    engine.OnGo(go, output);
                    break;
                case UciCommand.Stop stop:
                    engine.OnStop(output);
                    break;
                case UciCommand.Quit:
                    return;
                case UciCommand.Debug dbg:
                    engine.OnDebug(dbg.On);
                    break;
            }
        }
    }

    public static void WriteResponse(TextWriter output, UciResponse response)
    {
        output.WriteLine(UciFormatter.Format(response));
        output.Flush();
    }
}
