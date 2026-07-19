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

// Use the rendered white-knight launcher icon (mipmap PNGs generated from DIR.Lib's
// chess_white_knight baseline) instead of the default Android robot.
[assembly: Application(Label = "Chess", Icon = "@mipmap/ic_launcher")]

namespace Chess.Droid;

/// <summary>
/// The Android chess head — pilot consumer of <see cref="SdlVulkanActivity"/>. SDL's Java bridge
/// launches this activity, and the base brings up SDL3 + Vulkan and calls <see cref="OnRendererReady"/>
/// where we mount the shared startup menu (<see cref="StartupWizard"/> via DIR.Lib's
/// <see cref="PixelMenuWidget{TSurface}"/>) and then the game display
/// (<see cref="PixelGameDisplay{TSurface}"/>), routing touches into whichever is active.
///
/// Player-vs-Computer runs the engine IN-PROCESS (<see cref="AiEngine"/>, on a background thread) —
/// there is no engine child process on Android, exactly like Chess.Web. Custom games currently start
/// from the standard board (interactive piece-placement setup is a follow-up).
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
    private const int AiDepth = 3; // modest depth: responsive on a phone (tune later)

    protected override string WindowTitle => "Chess";

    // Match the display's own canvas background so the raw-cleared surface and the margins GameUI
    // paints don't band (same rationale as the desktop GUI).
    protected override RGBAColor32 BackgroundColor => PixelGameDisplay<VulkanContext>.Background;

    private VkRenderer _renderer = null!;
    private StartupWizard? _wizard;
    private PixelMenuWidget<VulkanContext>? _menu;
    private PixelGameDisplay<VulkanContext>? _display;
    private Game? _game;

    // Player-vs-Computer state. The engine runs in-process and SYNCHRONOUSLY right after the human's
    // move: at AiDepth=3 the search is a few tens of ms, so it doesn't need a background thread (and a
    // struct move can't be handed across threads via volatile — that'd need a lock, only worth it for a
    // slower/stronger engine later). It's the AI's turn while it runs, so no input races.
    private bool _vsComputer;
    private Side _humanSide;

    protected override void OnRendererReady(VkRenderer renderer, SdlEventLoop loop)
    {
        // Route the renderer's loop diagnostics to logcat (Android has no console) — surfaces the
        // background/foreground surface-recreation traces. The renderer's DebugLog is compiled in only
        // for DEBUG or ANDROID, so this costs nothing on desktop Release. Tag: "chessdroid".
        SdlEventLoop.DiagnosticLog = m => Android.Util.Log.Info("chessdroid", m);

        // PixelGameDisplay loads its glyph fonts from FontPaths.FontsDirectory
        // (AppContext.BaseDirectory/Fonts). That path is empty in the APK sandbox, so stage the
        // bundled asset copies into it first — the Android analog of Chess.Web's LoadFontsAsync.
        StageFonts();
        _renderer = renderer;

        // Resume an unfinished game across a process kill (Android reclaims backgrounded apps freely);
        // otherwise open the startup menu. A background->foreground return keeps the in-memory state, so
        // this only runs on a cold launch.
        if (TryLoadGame() is { } saved && !saved.Game.IsFinished)
            StartGame(saved.Game, saved.ComputerSide);
        else
            ShowMenu();

        loop.OnRender = Render;
        loop.OnResize = (w, h) => _display?.OnResize((int)w, (int)h);
        // SDL synthesizes mouse-button events from single-finger touches, so a tap arrives here as a
        // left button-down. Route it to the menu or the board depending on what's up.
        loop.OnMouseDown = (button, x, y, _, _) =>
        {
            if (button != 1) return false;
            return HandleTap((int)x, (int)y);
        };
        // The menu is static between taps (each tap already forces a redraw), so only the in-play
        // display needs the external-update poll.
        loop.CheckNeedsRedraw = () => _display?.HasPendingUpdate ?? false;
    }

    private void ShowMenu()
    {
        _display = null;
        _wizard = new StartupWizard(); // no Play-by-Link on Android (no link driver)
        _menu = new PixelMenuWidget<VulkanContext>(_renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = _wizard.Current;
        _menu.Reset(title, prompt, [.. items]);
    }

    private void Render()
    {
        if (_display is not null)
            _display.Render();
        else
            _menu?.Render();
    }

    private bool HandleTap(int x, int y)
    {
        if (_menu is not null && _wizard is not null)
        {
            if (!_menu.HandleInput(new InputEvent.MouseDown(x, y)))
                return false;
            if (_menu.IsConfirmed)
            {
                _wizard.Confirm(_menu.SelectedIndex);
                if (_wizard.IsComplete)
                {
                    var (_, computerSide, _) = _wizard.Result;
                    _menu = null;
                    _wizard = null;
                    StartGame(new Game(), computerSide); // Custom -> standard board for now
                }
                else
                {
                    var (title, prompt, items) = _wizard.Current;
                    _menu.Reset(title, prompt, [.. items]);
                }
            }
            return true;
        }

        // In-game: apply the human's tap, then let the engine reply (synchronously) if it's PvC.
        if (_display is null) return false;
        _display.UI.HandleMouseDown(x, y);
        SaveGame();
        PlayAiReply();
        return true;
    }

    private void StartGame(Game game, Side computerSide)
    {
        _game = game;
        _vsComputer = computerSide != Side.None;
        _humanSide = computerSide == Side.White ? Side.Black : Side.White;

        _menu = null;
        _wizard = null;
        _display = new PixelGameDisplay<VulkanContext>(_renderer);
        _display.ResetGame(_game);

        SaveGame();
        PlayAiReply(); // if the human chose Black, White (the AI) opens
    }

    // Plays the engine's reply in-process while it's the computer's turn. Synchronous: at AiDepth the
    // search is brief, and it's not the human's turn, so blocking the loop for it is acceptable and far
    // simpler than a background thread + cross-thread struct handoff. (Loop handles a chain in case a
    // future mode has the AI move more than once.)
    private void PlayAiReply()
    {
        if (!_vsComputer || _game is null) return;
        while (!_game.IsFinished && _game.CurrentSide != _humanSide)
        {
            var move = new AiEngine(_game.CurrentSide, maxDepth: AiDepth).PickMove(_game);
            if (move is not { } mv) break;
            _game.TryMove(mv);
            SaveGame();
        }
    }

    // The game is persisted to app-internal storage: a header line (mode marker: the computer's side)
    // then the UCI move list. Replaying the moves rebuilds the full position AND history (castling /
    // en-passant rights included) that a bare FEN snapshot would lose.
    private string GamePath => Path.Combine(FilesDir!.AbsolutePath, "game.uci");

    private (Game Game, Side ComputerSide)? TryLoadGame()
    {
        try
        {
            if (!File.Exists(GamePath)) return null;
            var lines = File.ReadAllLines(GamePath);
            if (lines.Length < 1) return null;
            var computerSide = Enum.TryParse<Side>(lines[0].Trim(), out var cs) ? cs : Side.None;
            var moves = lines.Length > 1 ? lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
            var game = new Game();
            foreach (var move in moves)
                if (!game.TryMove(UciMove.Parse(move)).IsMoveOrCapture())
                    return null; // a move didn't apply -> save is stale/incompatible; start fresh
            return (game, computerSide);
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
            var computerSide = _vsComputer ? (_humanSide == Side.White ? Side.Black : Side.White) : Side.None;
            var moves = string.Join(' ', _game.Plies.Select(p => UciMove.Format(p.Action)));
            File.WriteAllText(GamePath, $"{computerSide}\n{moves}");
        }
        catch
        {
            // Best-effort: a failed write must not take down the game.
        }
    }

    // Copies the bundled font assets (assets/Fonts/*.ttf) into FontPaths.FontsDirectory once, so the
    // managed rasterizer's file-based loading finds them. Idempotent across launches.
    private void StageFonts()
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
