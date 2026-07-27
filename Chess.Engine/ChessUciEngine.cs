using Chess.Lib;
using Chess.UCI;

namespace Chess.Engine;

/// <summary>
/// UCI engine implementation that uses <see cref="AiEngine"/> for move calculation.
/// </summary>
internal sealed class ChessUciEngine : IUciEngine
{
    private Game _game = new();
    private bool _debug;

    // Cancels the search currently in flight. Written by OnGo and read by OnStop, which UciServer
    // dispatches from the read loop while the search runs on another thread.
    private readonly Lock _searchLock = new();
    private CancellationTokenSource? _searchCts;

    public void OnUci(TextWriter output)
    {
        UciServer.WriteResponse(output, new UciResponse.Id("name", "SharpChess"));
        UciServer.WriteResponse(output, new UciResponse.Id("author", "sebgod"));
        UciServer.WriteResponse(output, new UciResponse.UciOk());
    }

    public void OnIsReady(TextWriter output)
    {
        UciServer.WriteResponse(output, new UciResponse.ReadyOk());
    }

    public void OnNewGame()
    {
        _game = new Game();
    }

    public void OnPosition(string? fen, string[] moves)
    {
        if (fen is not null)
        {
            _game = GameFromFen(fen);
        }
        else
        {
            _game = new Game();
        }

        foreach (var moveStr in moves)
        {
            var action = UciMove.Parse(moveStr);
            var result = _game.TryMove(action);
            if (!result.IsMoveOrCapture())
            {
                if (_debug)
                {
                    System.Console.Error.WriteLine($"info string failed to apply move {moveStr}: {result}");
                }
            }
        }
    }

    public void OnGo(UciCommand.Go goParams, TextWriter output)
    {
        var side = _game.CurrentSide;
        var budget = UciTimeBudget.ForMove(goParams, side);

        // Depth cap policy: an explicit "go depth n" means exactly that. Otherwise, if the GUI gave us
        // a clock, time is the real bound and depth is only there to terminate — iterative deepening
        // goes as deep as the budget allows. With neither, fall back to the fixed default so a bare
        // "go" still answers promptly.
        var depth = goParams.Depth ?? (budget is not null || goParams.Infinite
            ? AiEngine.MaxSearchDepth
            : AiEngine.DefaultDepth);

        var searchCts = new CancellationTokenSource();

        lock (_searchLock)
        {
            _searchCts = searchCts;
        }

        var aiEngine = new AiEngine(side, depth);
        AiEngine.SearchResult result;

        try
        {
            result = aiEngine.Search(
                _game,
                onDepthComplete: info => UciServer.WriteResponse(output, new UciResponse.Info(
                    $"depth {info.Depth} score {FormatScore(info.Score, info.Depth)} nodes {info.Nodes}")),
                moveTime: budget,
                cancellationToken: searchCts.Token);
        }
        finally
        {
            // Clear the field before disposing, both under the lock OnStop uses, so a concurrent stop
            // either cancels a live source or sees nothing — never cancels a disposed one.
            lock (_searchLock)
            {
                if (ReferenceEquals(_searchCts, searchCts))
                {
                    _searchCts = null;
                }
            }

            searchCts.Dispose();
        }

        // A search that was cut short still owes the GUI a move — "0000" is reserved for positions
        // with no legal move at all, and depth 1 always completes precisely so this stays true.
        UciServer.WriteResponse(output, new UciResponse.BestMove(
            result.BestMove is { } move ? UciMove.Format(move) : "0000"));
    }

    public void OnStop(TextWriter output)
    {
        // Cancelling is cooperative: the search notices within a few thousand nodes, keeps the best
        // move from its last completed iteration, and OnGo writes the bestmove as usual.
        lock (_searchLock)
        {
            _searchCts?.Cancel();
        }
    }

    /// <summary>
    /// Formats a score the way UCI wants it: a forced mate is reported as <c>mate n</c> in moves (not
    /// plies, and negative when we are the one being mated), anything else as centipawns.
    /// </summary>
    private static string FormatScore(int score, int depth)
    {
        var distanceFromMate = AiEngine.MateScore - Math.Abs(score);

        if (distanceFromMate > depth)
        {
            return $"cp {score}";
        }

        // distanceFromMate counts plies to the mate; UCI counts moves, rounding a half move up.
        var moves = (distanceFromMate + 1) / 2;
        return $"mate {(score > 0 ? moves : -moves)}";
    }

    public void OnDebug(bool on)
    {
        _debug = on;
    }

    private static Game GameFromFen(string fen)
    {
        var parts = fen.Split(' ');
        var board = Board.FromFenPlacement(parts[0]);
        var side = parts.Length > 1 && parts[1] == "b" ? Side.Black : Side.White;
        return new Game(board, side, []);
    }
}
