namespace Chess.UCI;

/// <summary>
/// Parses UCI protocol text lines into command/response objects.
/// Handles arbitrary whitespace between tokens and ignores unknown tokens per UCI spec.
/// </summary>
public static class UciParser
{
    public static UciCommand? ParseCommand(string line)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        return tokens[0] switch
        {
            "uci" => new UciCommand.UciInit(),
            "isready" => new UciCommand.IsReady(),
            "ucinewgame" => new UciCommand.UciNewGame(),
            "position" => ParsePosition(tokens),
            "go" => ParseGo(tokens),
            "stop" => new UciCommand.Stop(),
            "quit" => new UciCommand.Quit(),
            "debug" => new UciCommand.Debug(tokens.Length > 1 && tokens[1] == "on"),
            _ => null
        };
    }

    public static UciResponse? ParseResponse(string line)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        return tokens[0] switch
        {
            "id" when tokens.Length >= 3 => new UciResponse.Id(tokens[1], string.Join(" ", tokens[2..])),
            "uciok" => new UciResponse.UciOk(),
            "readyok" => new UciResponse.ReadyOk(),
            "bestmove" when tokens.Length >= 2 => ParseBestMove(tokens),
            "info" => new UciResponse.Info(string.Join(" ", tokens[1..])),
            _ => null
        };
    }

    private static UciCommand.SetPosition ParsePosition(string[] tokens)
    {
        string? fen = null;
        var moves = Array.Empty<string>();
        var i = 1;

        if (i < tokens.Length && tokens[i] == "startpos")
        {
            i++;
        }
        else if (i < tokens.Length && tokens[i] == "fen")
        {
            i++;
            var fenParts = new List<string>();
            while (i < tokens.Length && tokens[i] != "moves")
            {
                fenParts.Add(tokens[i]);
                i++;
            }
            fen = string.Join(" ", fenParts);
        }

        if (i < tokens.Length && tokens[i] == "moves")
        {
            i++;
            moves = tokens[i..];
        }

        return new UciCommand.SetPosition(fen, moves);
    }

    private static UciCommand.Go ParseGo(string[] tokens)
    {
        int? moveTime = null, depth = null, wTime = null, bTime = null;
        var infinite = false;

        for (var i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "movetime" when i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var mt):
                    moveTime = mt; i++; break;
                case "depth" when i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var d):
                    depth = d; i++; break;
                case "wtime" when i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var wt):
                    wTime = wt; i++; break;
                case "btime" when i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var bt):
                    bTime = bt; i++; break;
                case "infinite":
                    infinite = true; break;
            }
        }

        return new UciCommand.Go(moveTime, depth, infinite, wTime, bTime);
    }

    private static UciResponse.BestMove ParseBestMove(string[] tokens)
    {
        var move = tokens[1];
        string? ponder = null;

        if (tokens.Length >= 4 && tokens[2] == "ponder")
        {
            ponder = tokens[3];
        }

        return new UciResponse.BestMove(move, ponder);
    }
}
