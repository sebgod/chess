using Chess.Engine;
using Chess.UCI;

using var cts = new CancellationTokenSource();
System.Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var engine = new ChessUciEngine();
await UciServer.RunAsync(engine, System.Console.In, System.Console.Out, cts.Token);
