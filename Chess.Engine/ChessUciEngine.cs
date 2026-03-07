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
                    System.Console.Error.WriteLine($"info string failed to apply move {moveStr}: {result}");
                }
            }
        }
    }

    public void OnGo(UciCommand.Go goParams, TextWriter output)
    {
        var side = _game.CurrentSide;
        var depth = goParams.Depth ?? AiEngine.DefaultDepth;
        var aiEngine = new AiEngine(side, depth);

        var result = aiEngine.Search(_game, onDepthComplete: info =>
        {
            UciServer.WriteResponse(output, new UciResponse.Info(
                $"depth {info.Depth} score cp {info.Score} nodes {info.Nodes}"));
        });

        if (result.BestMove is { } move)
        {
            UciServer.WriteResponse(output, new UciResponse.BestMove(UciMove.Format(move)));
        }
        else
        {
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
