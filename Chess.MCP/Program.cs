using Chess.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInstructions = """
            This MCP server provides chess analysis tools. You can:
            - Analyze board positions from FEN strings
            - Get all legal moves for a position or specific piece
            - Find the best move using the chess engine (negamax with alpha-beta pruning)
            - Evaluate positions (material and positional score in centipawns)
            - Apply moves and track game state
            - Solve chess problems (mate in N, find winning moves)
            - Get game status (check, checkmate, stalemate)

            Positions use standard algebraic notation (e.g. "e4", "a1").
            Moves use UCI format (e.g. "e2e4", "e7e8q" for promotion).
            Board positions use FEN placement notation (e.g. "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR").
            """;
    })
    .WithStdioServerTransport()
    .WithTools<BoardTools>()
    .WithTools<MoveTools>()
    .WithTools<AnalysisTools>();

await builder.Build().RunAsync();
