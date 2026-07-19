using System;
using System.IO;
using System.Linq;
using Android.App;
using Android.Content.PM;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using DIR.Lib;
using SdlVulkan.Renderer;
using static Android.Content.PM.ConfigChanges;
using File = System.IO.File;

namespace Chess.Droid;

/// <summary>
/// The Android chess head — pilot consumer of <see cref="SdlVulkanActivity"/>. SDL's Java bridge
/// launches this activity, and the base brings up SDL3 + Vulkan and calls
/// <see cref="OnRendererReady"/> where we mount the shared <see cref="PixelGameDisplay{TSurface}"/>
/// (board + history + status) and route touches into <see cref="GameUI"/>.
///
/// Skeleton scope: hot-seat Player-vs-Player. No engine process (not viable on Android) and no
/// startup menu yet — a fresh game renders and taps move pieces. Menu + in-process AI are follow-ups.
/// </summary>
[Activity(
    Label = "Chess",
    MainLauncher = true,
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    Exported = true,
    ConfigurationChanges =
        LayoutDirection | Locale | GrammaticalGender | FontScale |
        FontWeightAdjustment | ConfigChanges.Orientation | UiMode |
        ScreenLayout | ScreenSize | SmallestScreenSize |
        Keyboard | KeyboardHidden | Navigation)]
public sealed class MainActivity : SdlVulkanActivity
{
    protected override string WindowTitle => "Chess";

    // Match the display's own canvas background so the raw-cleared surface and the margins GameUI
    // paints don't band (same rationale as the desktop GUI).
    protected override RGBAColor32 BackgroundColor => PixelGameDisplay<VulkanContext>.Background;

    private Game? _game;

    protected override void OnRendererReady(VkRenderer renderer, SdlEventLoop loop)
    {
        // PixelGameDisplay loads its glyph fonts from FontPaths.FontsDirectory
        // (AppContext.BaseDirectory/Fonts). That path is empty in the APK sandbox, so stage the
        // bundled asset copies into it first — the Android analog of Chess.Web's LoadFontsAsync.
        StageFonts();

        // Restore the in-progress game if one was persisted (Android reclaims backgrounded apps
        // freely, so this is what makes a game survive leaving and coming back), else start fresh.
        _game = LoadGame() ?? new Game();
        var display = new PixelGameDisplay<VulkanContext>(renderer);
        display.ResetGame(_game);

        loop.OnRender = display.Render;
        loop.OnResize = (w, h) => display.OnResize((int)w, (int)h);
        // SDL synthesizes mouse-button events from single-finger touches, so a tap arrives here as a
        // left button-down — GameUI's tap-to-select / tap-to-move handles the rest (hot-seat).
        loop.OnMouseDown = (button, x, y, _, _) =>
        {
            if (button != 1) return false;
            display.UI.HandleMouseDown((int)x, (int)y);
            // Persist after every tap so the game survives backgrounding / a resume crash / a kill.
            // Cheap — a short UCI move list; GameUI mutates the same _game instance in place.
            SaveGame();
            return true;
        };
        loop.CheckNeedsRedraw = () => display.HasPendingUpdate;
    }

    // The game is persisted to app-internal storage as a UCI move list; replaying it rebuilds the full
    // position AND history (castling / en-passant rights included) that a bare FEN snapshot would lose.
    private string GamePath => Path.Combine(FilesDir!.AbsolutePath, "game.uci");

    private Game? LoadGame()
    {
        try
        {
            if (!File.Exists(GamePath)) return null;
            var moves = File.ReadAllText(GamePath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (moves.Length == 0) return null;
            var game = new Game();
            foreach (var move in moves)
                if (!game.TryMove(UciMove.Parse(move)).IsMoveOrCapture())
                    return null; // a move didn't apply -> save is stale/incompatible; start fresh
            return game;
        }
        catch
        {
            return null; // unreadable / garbled save -> start fresh
        }
    }

    private void SaveGame()
    {
        if (_game is null) return;
        try
        {
            File.WriteAllText(GamePath, string.Join(' ', _game.Plies.Select(p => UciMove.Format(p.Action))));
        }
        catch
        {
            // Best-effort: a failed write must not take down the game.
        }
    }

    // Copies the bundled font assets (assets/Fonts/*.ttf) into FontPaths.FontsDirectory once, so the
    // managed rasterizer's file-based loading finds them. Idempotent across launches.
    void StageFonts()
    {
        var dir = FontPaths.FontsDirectory;
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "DejaVuSans.ttf", "Merida.ttf" })
        {
            var dest = Path.Combine(dir, name);
            if (File.Exists(dest)) continue;
            using var asset = Assets!.Open($"Fonts/{name}");
            using var file = File.Create(dest);
            asset.CopyTo(file);
        }
    }
}
