using Chess.Engine;
using Chess.UCI;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var engine = new ChessUciEngine();
await UciServer.RunAsync(engine, Console.In, Console.Out, cts.Token);
