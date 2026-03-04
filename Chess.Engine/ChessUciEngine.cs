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
                    Console.Error.WriteLine($"info string failed to apply move {moveStr}: {result}");
                }
            }
        }
    }

    public void OnGo(UciCommand.Go goParams, TextWriter output)
    {
        var side = _game.CurrentSide;
        var aiEngine = new AiEngine(side);
        var action = aiEngine.PickMove(_game);

        if (action is { } move)
        {
            var uciMove = UciMove.Format(move);
            UciServer.WriteResponse(output, new UciResponse.BestMove(uciMove));
        }
        else
        {
            // No legal move available — send a null move placeholder
            UciServer.WriteResponse(output, new UciResponse.BestMove("0000"));
        }
    }

    public void OnStop(TextWriter output)
    {
        // Search is instant for now, so stop is a no-op
        // But we should still respond with bestmove if we have one
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
