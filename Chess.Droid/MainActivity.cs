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
using GameMode = Chess.Lib.GameMode; // Android.App.GameMode collides

// Use the rendered white-knight launcher icon (mipmap PNGs generated from DIR.Lib's
// chess_white_knight baseline) instead of the default Android robot.
[assembly: Application(Label = "Chess", Icon = "@mipmap/ic_launcher", Theme = "@style/AppTheme")]

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

        // Match the navigation-bar (home button row) background to the app background — the system
        // default otherwise clashes below our status bar when the bars are visible. UI-thread call.
        RunOnUiThread(() =>
        {
            var c = PixelGameDisplay<VulkanContext>.Background;
#pragma warning disable CA1422 // deprecated in API 35 (edge-to-edge enforcement); fine through 34
            Window?.SetNavigationBarColor(Android.Graphics.Color.Argb(c.Alpha, c.Red, c.Green, c.Blue));
#pragma warning restore CA1422
        });

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
        loop.OnResize = (w, h) =>
        {
            if (_display is null) return;
            // Re-query the safe area on every resize — the cutout/gesture-bar insets move with
            // rotation and can change on fold/resume. The setter relayouts only when they differ.
            _display.SafeAreaInsets = SdlWindow.GetSafeAreaInsets();
            _display.TopCutout = QueryTopCutout();
            _display.OnResize((int)w, (int)h);
        };
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
        // Android's back button/gesture: SDL traps it before the activity's onBackPressed and
        // delivers it as a key (AC_BACK -> InputKey.Escape), already on the SDL thread. Desktop Esc
        // semantics, staged: playback -> live game -> menu (state is saved move-by-move) -> launcher.
        loop.OnKeyDown = (key, _) =>
        {
            if (key != InputKey.Escape) return false;
            if (_display is { } d)
            {
                if (d.UI.Mode == GameUIMode.Playback)
                    d.UI.ExitPlayback();
                else
                    ShowMenu();
                return true;
            }
            RunOnUiThread(() => MoveTaskToBack(true)); // menu: hand back to the launcher
            return true;
        };
    }

    private void ShowMenu()
    {
        _display = null;
        // No Play-by-Link on Android (no link driver). "Continue game" appears whenever an
        // unfinished save exists (back button mid-game, or a cold launch with one on disk) —
        // returning to the menu must never cost the game; only starting a new one overwrites it.
        var canContinue = TryLoadGame() is { } s && !s.Game.IsFinished;
        _wizard = new StartupWizard(includeContinue: canContinue);
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
                    var (mode, computerSide, _) = _wizard.Result;
                    _menu = null;
                    _wizard = null;
                    if (mode == GameMode.Continue)
                    {
                        // The menu only offers Continue when the save parsed moments ago; if it
                        // fails NOW, re-showing the menu (without Continue) is the safe move — a
                        // silent fresh StartGame would overwrite the very game being continued.
                        if (TryLoadGame() is { } saved)
                            StartGame(saved.Game, saved.ComputerSide);
                        else
                            ShowMenu();
                    }
                    else
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
        // Safe area BEFORE ResetGame so the first layout already clears the notch and the rounded
        // bottom; the notch strip shows the mode left and the move counter right of the camera.
        _display.SafeAreaInsets = SdlWindow.GetSafeAreaInsets();
        _display.TopCutout = QueryTopCutout();
        // Short labels: the notch strip is status-bar-sized chrome, not a headline.
        _display.TopStripLabel = _vsComputer ? $"vs AI ({_humanSide})" : "PvP";
        // Touch-only: no keyboard hints in the status bar; playback exits via the history chip.
        _display.KeyboardHints = false;
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

    // Persistence lives in the shared Chess.UCI.GameStore so every front-end (desktop GUI too) uses
    // the same file format and replay logic; these wrappers just supply the path, the computer side
    // (derived from this activity's mode state), and the logcat sink.
    private (Game Game, Side ComputerSide)? TryLoadGame()
        => GameStore.TryLoad(GamePath, m => SdlEventLoop.DiagnosticLog?.Invoke(m));

    private void SaveGame()
    {
        if (_game is null) return;
        var computerSide = _vsComputer ? (_humanSide == Side.White ? Side.Black : Side.White) : Side.None;
        GameStore.Save(GamePath, _game, computerSide, m => SdlEventLoop.DiagnosticLog?.Invoke(m));
    }

    // The exact camera punch-hole bounds, so the notch strip lines its text up with the camera's row
    // (the safe-area top inset is deeper than the cutout — strip-centered text sits visibly below the
    // camera) and keeps out of its true horizontal span. Null when unavailable (pre-API-29, insets
    // not attached yet, no cutout) — the strip then falls back to generic centering.
    private (int Left, int Top, int Right, int Bottom)? QueryTopCutout()
    {
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
            var r = Window?.DecorView.RootWindowInsets?.DisplayCutout?.BoundingRectTop;
            return r is not null && r.Width() > 0 ? (r.Left, r.Top, r.Right, r.Bottom) : null;
        }
        catch
        {
            return null;
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
