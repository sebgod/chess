using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;
using System.Runtime.InteropServices;
using System.Text;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Query cell size before entering alternate buffer to keep response invisible
var (cellWidth, cellHeight) = await QueryCellSizeAsync() ?? (10, 20);

var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

// Enable mouse input - use native Windows API on Windows, VT sequences elsewhere
if (isWindows)
{
    WindowsConsoleInput.EnableMouseInput();
}
else
{
    Console.Write("\e[?1006h"); // Enable SGR mouse tracking (non-Windows)
}

Console.Write("\e[?1049h"); // Enter alternate buffer
Console.Write("\e[?25l");   // Hide cursor

try
{
    // Reserve right-side columns for ply history
    const int historyColumns = 24;
    const int statusBarRows = 1;

    var imageColumns = Console.WindowWidth - historyColumns;
    var imageRows = Console.WindowHeight - statusBarRows;
    var width = (uint)imageColumns * (uint)cellWidth;
    var height = (uint)imageRows * (uint)cellHeight;
    var game = new Game();
    using var image = new MagickImage(MagickColors.Black, width, height);

    var imageRenderer = new MagickImageRenderer();
    var ui = new GameUI(game, image.Width, image.Height, 
        mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff));

    var historyStartColumn = imageColumns;
    var statusBarRow = Console.WindowHeight - 1;
    var historyRowCount = imageRows;

    RenderFrame(ui, imageRenderer, image, default, cellHeight);
    RenderStatusBar(game, statusBarRow, Console.WindowWidth);
    RenderHistory(game, historyStartColumn, historyColumns, historyRowCount);

    while (!cts.Token.IsCancellationRequested)
    {
        var hasInput = isWindows ? WindowsConsoleInput.HasInputEvents() : Console.KeyAvailable;
        if (hasInput)
        {
            var mouseEvent = isWindows ? WindowsConsoleInput.TryReadMouseEvent() : ParseVTMouseEvent();
            if (mouseEvent is { Button: 0, IsRelease: false })
            {
                var pixelX = mouseEvent.Value.X * cellWidth;
                var pixelY = mouseEvent.Value.Y * cellHeight;

                var (response, clipRects) = ui.TryPerformAction(pixelX, pixelY);
                if (response.HasFlag(UIResponse.NeedsRefresh))
                {
                    RenderFrame(ui, imageRenderer, image, clipRects, cellHeight);
                    if (response.HasFlag(UIResponse.IsUpdate))
                    {
                        RenderStatusBar(game, statusBarRow, Console.WindowWidth);
                        RenderHistory(game, historyStartColumn, historyColumns, historyRowCount);
                    }
                }
            }
        }
        else
        {
            try
            {
                await Task.Delay(16, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
finally
{
    // Restore mouse input settings
    if (isWindows)
    {
        WindowsConsoleInput.RestoreConsoleMode();
    }
    else
    {
        Console.Write("\e[?1006l"); // Disable SGR mouse tracking (non-Windows)
    }

    Console.Write("\e[?25h");   // Show cursor
    Console.Write("\e[?1049l"); // Leave alternate buffer
}

static async Task<(int Width, int Height)?> QueryCellSizeAsync()
{
    // Query cell size using XTWINOPS: CSI 16 t
    // Response: CSI 6 ; height ; width t
    var response = await GetControlSequenceResponseAsync("\e[16t");

    // Find the terminator 't' and parse the response
    var tIndex = response.IndexOf('t');
    if (tIndex < 0)
    {
        return null;
    }

    // Parse: ESC [ 6 ; height ; width t
    var content = response[..tIndex];
    var parts = content.TrimStart('\e', '[').Split(';');
    if (parts.Length == 3 &&
        parts[0] == "6" &&
        int.TryParse(parts[1], out var height) &&
        int.TryParse(parts[2], out var width))
    {
        return (width, height);
    }

    return null;
}

static void RenderFrame(GameUI ui, MagickImageRenderer renderer, MagickImage image, IReadOnlyList<RectInt>? clipRects, int cellHeight)
{
    // Calculate clip region for rendering optimization (reduces work in ui.Render)
    RectInt clip;
    bool isFullRender;
    if (clipRects is { Count: > 0 })
    {
        isFullRender = false;
        clip = clipRects[0];
        for (var i = 1; i < clipRects.Count; i++)
        {
            clip = clip.Union(clipRects[i]);
        }
    }
    else
    {
        isFullRender = true;
        clip = new RectInt((image.Width, image.Height), (0, 0));
    }

    ui.Render(renderer, image, clip);

    if (isFullRender)
    {
        // Full image output
        Console.SetCursorPosition(0, 0);
        var sixels = Encoding.ASCII.GetString(image.ToByteArray(MagickFormat.Sixel));
        Console.Write(sixels);
    }
    else
    {
        // Partial output - crop to affected rows and render only that portion
        // Align to cell boundaries for proper cursor positioning
        var startRow = (int)(clip.UpperLeft.Y / cellHeight);
        var endRow = (int)((clip.LowerRight.Y + cellHeight - 1) / cellHeight);

        var pixelStartY = startRow * cellHeight;
        var pixelEndY = Math.Min((int)image.Height, endRow * cellHeight);
        var cropHeight = pixelEndY - pixelStartY;

        if (cropHeight > 0)
        {
            using var cropped = image.Clone();
            cropped.Crop(new MagickGeometry(0, pixelStartY, image.Width, (uint)cropHeight));
            cropped.ResetPage();

            Console.SetCursorPosition(0, startRow);
            var sixels = Encoding.ASCII.GetString(cropped.ToByteArray(MagickFormat.Sixel));
            Console.Write(sixels);
        }
    }
}

static async ValueTask<string> GetControlSequenceResponseAsync(string sequence)
{
    const int maxTries = 10;

    var response = new StringBuilder();
    Console.Write(sequence);

    var tries = 0;
    while (!Console.KeyAvailable && tries++ < maxTries)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(10));
    }

    while (Console.KeyAvailable)
    {
        var key = Console.ReadKey(true);
        response.Append(key.KeyChar);
    }

    return response.ToString();
}

static (int Button, int X, int Y, bool IsRelease)? ParseVTMouseEvent()
{
    var sb = new StringBuilder();

    // Read characters looking for SGR mouse escape sequence: ESC [ < Params M/m
    var first = Console.ReadKey(intercept: true);
    if (first.Key != ConsoleKey.Escape)
    {
        return null;
    }

    // Read until we get 'M' (press) or 'm' (release)
    while (true)
    {
        if (!Console.KeyAvailable)
        {
            return null;
        }

        var ch = Console.ReadKey(intercept: true);
        if (ch.KeyChar is 'M' or 'm')
        {
            var isRelease = ch.KeyChar == 'm';
            var parts = sb.ToString().TrimStart('[', '<').Split(';');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out var button) &&
                int.TryParse(parts[1], out var x) &&
                int.TryParse(parts[2], out var y))
            {
                // SGR coordinates are 1-based
                return (button, x - 1, y - 1, isRelease);
            }
            return null;
        }

        sb.Append(ch);
    }
}

static void RenderStatusBar(Game game, int row, int width)
{
    Console.SetCursorPosition(0, row);

    var currentPlayer = game.CurrentSide == Side.White ? "White" : "Black";
    var status = game.GameStatus switch
    {
        GameStatus.Check => $" {currentPlayer} to move (CHECK)",
        GameStatus.Checkmate => $" {(game.CurrentSide == Side.White ? "Black" : "White")} wins by checkmate!",
        GameStatus.Stalemate => " Draw by stalemate",
        _ => $" {currentPlayer} to move"
    };

    // White text on dark gray background
    Console.Write($"\e[97;100m{status.PadRight(width)}\e[0m");
}

static void RenderHistory(Game game, int startColumn, int columnWidth, int rowCount)
{
    var plies = game.Plies;
    var moveCount = (plies.Count + 1) / 2;
    var startMove = Math.Max(0, moveCount - rowCount);

    // Render header
    Console.SetCursorPosition(startColumn, 0);
    Console.Write($"\e[97;100m{" Move History".PadRight(columnWidth)}\e[0m");

    for (var row = 1; row < rowCount; row++)
    {
        Console.SetCursorPosition(startColumn, row);

        var moveIdx = startMove + row - 1;
        var plyIdx = moveIdx * 2;

        if (plyIdx < plies.Count)
        {
            var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(plyIdx);
            var blackPly = plyIdx + 1 < plies.Count ? plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

            var line = $" {idxStr} {whitePly,-8} {blackPly,-8}";
            Console.Write($"\e[37;40m{line.PadRight(columnWidth)}\e[0m");
        }
        else
        {
            // Clear the row
            Console.Write($"\e[37;40m{new string(' ', columnWidth)}\e[0m");
        }
    }
}