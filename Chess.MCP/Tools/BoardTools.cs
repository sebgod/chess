using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using DIR.Lib;
using ModelContextProtocol.Server;
using SharpAstro.Png;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.MCP.Tools;

[McpServerToolType]
public class BoardTools
{
    [McpServerTool, Description("Display the current state of a chess board from a FEN placement string. Returns a visual Unicode representation and piece list.")]
    public static string DisplayBoard(
        [Description("FEN placement string (e.g. 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR' for starting position). Use 'startpos' for the standard starting position.")] string fen)
    {
        var board = ParseBoard(fen);
        var sb = new StringBuilder();

        sb.AppendLine(board.ToString());
        sb.AppendLine();
        sb.AppendLine($"FEN: {board.ToFEN()}");
        sb.AppendLine();

        AppendPieceList(sb, board, Side.White);
        AppendPieceList(sb, board, Side.Black);

        return sb.ToString();
    }

    [McpServerTool, Description("Render a board as PNG and return a base64 data URL. Uses DIR RGBA rendering for board drawing. If savePath is provided, the PNG is written to disk and the return value is the file path.")]
    public static string RenderBoardPng(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Image size in pixels (default: 480)")] uint size = 480,
        [Description("Optional UCI move arrow overlay (e.g. 'e2e4'). For multiple arrows, use 'moves' instead.")] string? move = null,
        [Description("Optional comma-separated UCI move list to overlay multiple arrows on a single board (e.g. 'e2e4,e7e5,g1f3'). Mutually exclusive with 'move'; if both are given, 'moves' wins.")] string? moves = null,
        [Description("Optional file path to save the PNG to disk")] string? savePath = null,
        [Description("Optional annotation text. When omitted and a move is given, defaults to SAN (e.g. 'Bd5#').")] string? annotation = null)
    {
        var board = ParseBoard(fen);
        var moveList = ParseUciList(moves) ?? (string.IsNullOrWhiteSpace(move) ? [] : [UciMove.Parse(move)]);

        var resolvedAnnotation = annotation;
        if (string.IsNullOrWhiteSpace(resolvedAnnotation) && moveList.Count > 0)
        {
            resolvedAnnotation = BuildSequenceAnnotation(board, moveList);
        }

        var png = RenderToPng(board, size, moveList, resolvedAnnotation);

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(savePath, png);
            return savePath;
        }

        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    [McpServerTool, Description("Render one PNG per ply for a UCI move sequence. Each PNG shows the position before that move with the move arrow and a SAN annotation. Returns JSON [{ply, file, san, fenBefore}, ...].")]
    public static string RenderSequence(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side that plays the first move: 'white' or 'black'")] string startingSide,
        [Description("Comma-separated UCI moves (e.g. 'h2h8,f8g7,d8f6')")] string moves,
        [Description("Output directory for the PNG files. Created if missing.")] string outDir,
        [Description("Filename prefix; files are named '{baseName}-move{round}{w|b}.png'")] string baseName,
        [Description("Image size in pixels (default: 480)")] uint size = 480,
        [Description("Optional annotation prefix appended before SAN (e.g. 'Puzzle 7' → 'Puzzle 7 - 1.Rh8+')")] string? annotationPrefix = null)
    {
        var board = ParseBoard(fen);
        var firstSide = ParseSide(startingSide);
        var actions = ParseUciList(moves) ?? throw new ArgumentException("moves cannot be empty.", nameof(moves));

        if (!Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        var plies = ImmutableList<RecordedPly>.Empty;
        var current = board;
        var currentSide = firstSide;
        var moveNum = 1;

        var entries = new List<SequenceEntry>(actions.Count);

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var fenBefore = current.ToFEN();
            var san = action.ToSan(current, plies);

            var isWhite = currentSide == Side.White;
            var sideTag = isWhite ? "w" : "b";
            var fileName = $"{baseName}-move{moveNum}{sideTag}.png";
            var filePath = Path.Combine(outDir, fileName);

            var moveLabel = isWhite ? $"{moveNum}.{san}" : $"{moveNum}...{san}";
            var fullAnnotation = string.IsNullOrWhiteSpace(annotationPrefix)
                ? moveLabel
                : $"{annotationPrefix} - {moveLabel}";

            var png = RenderToPng(current, size, [action], fullAnnotation);
            System.IO.File.WriteAllBytes(filePath, png);

            entries.Add(new SequenceEntry(i + 1, filePath, san, fenBefore));

            var (evalResult, newBoard, newPlies) = current.EvaluateAction(plies, action);
            if (!evalResult.Result.IsMoveOrCapture())
            {
                throw new InvalidOperationException($"Illegal move at ply {i + 1}: {UciMove.Format(action)} ({evalResult.Result})");
            }
            current = newBoard;
            plies = newPlies;
            currentSide = currentSide.ToOpposite();
            // Move number increments after black plays.
            if (!isWhite) moveNum++;
        }

        return JsonSerializer.Serialize(entries, SequenceJsonContext.Default.ListSequenceEntry);
    }

    private static List<Action>? ParseUciList(string? moves)
    {
        if (string.IsNullOrWhiteSpace(moves)) return null;
        var parts = moves.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<Action>(parts.Length);
        foreach (var p in parts) list.Add(UciMove.Parse(p));
        return list;
    }

    private static string BuildSequenceAnnotation(Board board, IReadOnlyList<Action> actions)
    {
        var sb = new StringBuilder();
        var current = board;
        var plies = ImmutableList<RecordedPly>.Empty;
        for (var i = 0; i < actions.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var san = actions[i].ToSan(current, plies);
            if (i % 2 == 0) sb.Append((i / 2) + 1).Append('.').Append(san);
            else sb.Append(san);
            var (evalResult, newBoard, newPlies) = current.EvaluateAction(plies, actions[i]);
            if (!evalResult.Result.IsMoveOrCapture()) break;
            current = newBoard;
            plies = newPlies;
        }
        return sb.ToString();
    }

    private static byte[] RenderToPng(Board board, uint size, IReadOnlyList<Action> moveList, string? annotation)
    {
        var game = new Game(board, Side.White, []);

        var hasAnnotation = !string.IsNullOrWhiteSpace(annotation);
        var annotationHeight = hasAnnotation ? (uint)(size * 0.07f) : 0u;
        var totalHeight = size + annotationHeight;

        using var renderer = new RgbaImageRenderer(size, totalHeight);
        var ui = new GameUI(game, size, size,
            mainFontColor: GameUI.PlainFontColor,
            backgroundColor: GameUI.PlainBackgroundColor);

        if (moveList.Count > 0)
        {
            var arrows = new List<(Position From, Position To, bool IsCapture)>(moveList.Count);
            foreach (var action in moveList)
            {
                var targetPiece = board[action.To];
                arrows.Add((action.From, action.To, targetPiece != Piece.None));
            }
            ui.ExplicitArrows = arrows;
        }

        var boardClip = new RectInt(((int)size, (int)size), PointInt.Origin);
        ui.Render<RgbaImage, RgbaImageRenderer>(renderer, boardClip);

        if (hasAnnotation)
        {
            var fontSize = size * 0.04f;
            var annotationRect = new RectInt(((int)size, (int)annotationHeight), new PointInt(0, (int)size));
            renderer.DrawText(annotation!, FontPaths.DejaVuSans, fontSize, GameUI.PlainFontColor, annotationRect, vertAlignment: TextAlign.Center);
        }

        return PngWriter.Encode(renderer.Surface.Pixels, renderer.Surface.Width, renderer.Surface.Height);
    }

    [McpServerTool, Description("Get the piece at a specific square on the board.")]
    public static string GetPieceAt(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Square in algebraic notation (e.g. 'e4', 'a1')")] string square)
    {
        var board = ParseBoard(fen);
        var position = ParsePosition(square);
        var piece = board[position];

        if (piece == Piece.None)
            return $"Square {square} is empty.";

        return $"Square {square}: {piece.Side} {piece.PieceType} ({piece.ToFEN()})";
    }

    [McpServerTool, Description("Evaluate a board position statically. Returns the material and positional score in centipawns from the perspective of the specified side.")]
    public static string EvaluatePosition(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side to evaluate for: 'white' or 'black' (default: 'white')")] string side = "white")
    {
        var board = ParseBoard(fen);
        var evalSide = ParseSide(side);
        var score = AiEngine.Evaluate(board, evalSide);

        var sb = new StringBuilder();
        sb.AppendLine($"Evaluation for {evalSide}: {FormatScore(score)}");
        sb.AppendLine();
        sb.AppendLine("Piece values: Pawn=100, Knight=320, Bishop=330, Rook=500, Queen=900");
        sb.AppendLine("Score includes piece-square table positional bonuses.");

        return sb.ToString();
    }

    [McpServerTool, Description("Convert a board position to FEN placement notation.")]
    public static string ToFen(
        [Description("FEN placement string or 'startpos'")] string fen)
    {
        var board = ParseBoard(fen);
        return board.ToFEN();
    }

    internal static Board ParseBoard(string fen)
    {
        if (string.Equals(fen, "startpos", StringComparison.OrdinalIgnoreCase))
            return Board.StandardBoard;

        return Board.FromFenPlacement(fen);
    }

    internal static Position ParsePosition(string square)
    {
        if (square.Length != 2)
            throw new ArgumentException($"Invalid square notation: '{square}'. Expected format like 'e4'.");

        var file = square[0] switch
        {
            >= 'a' and <= 'h' => (File)(square[0] - 'a'),
            _ => throw new ArgumentException($"Invalid file: '{square[0]}'. Must be a-h.")
        };

        var rank = square[1] switch
        {
            >= '1' and <= '8' => (Rank)(square[1] - '1'),
            _ => throw new ArgumentException($"Invalid rank: '{square[1]}'. Must be 1-8.")
        };

        return new Position(file, rank);
    }

    internal static Side ParseSide(string side) => side.ToLowerInvariant() switch
    {
        "white" or "w" => Side.White,
        "black" or "b" => Side.Black,
        _ => throw new ArgumentException($"Invalid side: '{side}'. Use 'white' or 'black'.")
    };

    internal static string FormatScore(int centipawns)
    {
        if (Math.Abs(centipawns) >= AiEngine.MateScore - 100)
        {
            var mateIn = (AiEngine.MateScore - Math.Abs(centipawns) + 1) / 2;
            return centipawns > 0 ? $"Mate in {mateIn}" : $"Mated in {mateIn}";
        }

        var sign = centipawns >= 0 ? "+" : "";
        return $"{sign}{centipawns / 100.0:F2} pawns ({centipawns} centipawns)";
    }

    private static void AppendPieceList(StringBuilder sb, Board board, Side side)
    {
        sb.Append($"{side}: ");
        var pieces = board.AllPiecesOfSide(side)
            .OrderByDescending(p => p.Item2.PieceType)
            .Select(p => $"{p.Item2.PieceType}({p.Item1})")
            .ToList();
        sb.AppendLine(string.Join(", ", pieces));
    }

    public sealed record SequenceEntry(int Ply, string File, string San, string FenBefore);

    public sealed record MateMove(int Ply, string Side, string Uci, string San, string Status);
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<BoardTools.SequenceEntry>))]
[JsonSerializable(typeof(List<BoardTools.MateMove>))]
internal partial class SequenceJsonContext : JsonSerializerContext
{
}
