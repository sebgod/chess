namespace Chess.UCI;

/// <summary>
/// Formats UCI commands and responses to text lines for transmission.
/// </summary>
public static class UciFormatter
{
    public static string Format(UciCommand command) => command switch
    {
        UciCommand.UciInit => "uci",
        UciCommand.IsReady => "isready",
        UciCommand.UciNewGame => "ucinewgame",
        UciCommand.SetPosition pos => FormatPosition(pos),
        UciCommand.Go go => FormatGo(go),
        UciCommand.Stop => "stop",
        UciCommand.Quit => "quit",
        UciCommand.Debug dbg => dbg.On ? "debug on" : "debug off",
        _ => throw new ArgumentException($"Unknown command type: {command.GetType()}", nameof(command))
    };

    public static string Format(UciResponse response) => response switch
    {
        UciResponse.Id id => $"id {id.Type} {id.Value}",
        UciResponse.UciOk => "uciok",
        UciResponse.ReadyOk => "readyok",
        UciResponse.BestMove bm => bm.Ponder is { } p ? $"bestmove {bm.Move} ponder {p}" : $"bestmove {bm.Move}",
        UciResponse.Info info => $"info string {info.Message}",
        _ => throw new ArgumentException($"Unknown response type: {response.GetType()}", nameof(response))
    };

    private static string FormatPosition(UciCommand.SetPosition pos)
    {
        var result = "position ";

        if (pos.Fen is { } fen)
        {
            result += "fen " + fen;
        }
        else
        {
            result += "startpos";
        }

        if (pos.Moves.Length > 0)
        {
            result += " moves " + string.Join(" ", pos.Moves);
        }

        return result;
    }

    private static string FormatGo(UciCommand.Go go)
    {
        var parts = new List<string> { "go" };

        if (go.Infinite)
        {
            parts.Add("infinite");
        }
        else
        {
            if (go.MoveTime is { } mt)
            {
                parts.Add("movetime");
                parts.Add(mt.ToString());
            }
            if (go.Depth is { } d)
            {
                parts.Add("depth");
                parts.Add(d.ToString());
            }
            if (go.WTime is { } wt)
            {
                parts.Add("wtime");
                parts.Add(wt.ToString());
            }
            if (go.BTime is { } bt)
            {
                parts.Add("btime");
                parts.Add(bt.ToString());
            }
        }

        return string.Join(" ", parts);
    }
}
