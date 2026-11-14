using System;
using System.IO;
using Android.App;
using Android.Content.PM;
using Android.Net.Wifi;
using Chess.Lib.UI;
using Chess.Touch;
using DIR.Lib;
using SdlVulkan.Renderer;
using static Android.Content.PM.ConfigChanges;
using File = System.IO.File;

// Use the rendered white-knight launcher icon (mipmap PNGs generated from DIR.Lib's
// chess_white_knight baseline) instead of the default Android robot.
[assembly: Application(Label = "Chess", Icon = "@mipmap/ic_launcher", Theme = "@style/AppTheme")]

// LAN network play (Chess.Net): sockets + Wi-Fi state, plus the multicast lock's permission — UDP
// broadcast discovery is dropped on many devices without CHANGE_WIFI_MULTICAST_STATE.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessNetworkState)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessWifiState)]
[assembly: UsesPermission(Android.Manifest.Permission.ChangeWifiMulticastState)]

namespace Chess.Droid;

/// <summary>
/// The Android chess head — pilot consumer of <see cref="SdlVulkanActivity"/>. SDL's Java bridge
/// launches this activity, and the base brings up SDL3 + Vulkan and calls <see cref="OnRendererReady"/>,
/// which hands the renderer to the shared touch game (<see cref="TouchChessGame"/>, in Chess.Touch,
/// which Chess.iOS runs too). What stays here is what only Android does: logcat, the navigation-bar
/// colour, the fonts and backend config shipped as APK assets, the name dialog, the multicast lock,
/// the camera cutout and the launcher hand-back (<see cref="ITouchPlatform"/>).
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
public sealed class MainActivity : SdlVulkanActivity, ITouchPlatform
{
    protected override string WindowTitle => "Chess";

    // Match the display's own canvas background so the raw-cleared surface and the margins GameUI
    // paints don't band (same rationale as the desktop GUI).
    protected override RGBAColor32 BackgroundColor => PixelGameDisplay<VulkanContext>.Background;

    private WifiManager.MulticastLock? _multicastLock;

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
        new TouchChessGame(this, TryLoadCloudConfig()).Attach(renderer, loop, SdlWindow);
    }

    // ── ITouchPlatform ──────────────────────────────────────────────────────────────────────────

    string ITouchPlatform.DataDirectory
        => FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The activity has no files directory yet.");

    string ITouchPlatform.DefaultPlayerName => Android.OS.Build.Model ?? "Player";

    void ITouchPlatform.PromptName(string title, string current, Action<string> join, Action cancel)
    {
        RunOnUiThread(() =>
        {
            var input = new Android.Widget.EditText(this) { Text = current };
            input.SetSingleLine(true);
            new AlertDialog.Builder(this)
                .SetTitle(title)!
                .SetView(input)!
                .SetPositiveButton("Join", (_, _) => join(input.Text ?? ""))!
                .SetNegativeButton("Cancel", (_, _) => cancel())!
                .SetCancelable(false)!
                .Show();
        });
    }

    void ITouchPlatform.AcquireLanDiscovery()
    {
        try
        {
            var wifi = (WifiManager?)(ApplicationContext?.GetSystemService(Android.Content.Context.WifiService));
            _multicastLock = wifi?.CreateMulticastLock("chess-lan");
            _multicastLock?.Acquire();
        }
        catch { /* best-effort: without it, some devices drop broadcast receives */ }
    }

    void ITouchPlatform.ReleaseLanDiscovery()
    {
        try { if (_multicastLock is { IsHeld: true }) _multicastLock.Release(); } catch { /* ignore */ }
        _multicastLock = null;
    }

    // The exact camera punch-hole bounds, so the notch strip lines its text up with the camera's row
    // (the safe-area top inset is deeper than the cutout — strip-centered text sits visibly below the
    // camera) and keeps out of its true horizontal span. Null when unavailable (pre-API-29, insets
    // not attached yet, no cutout) — the strip then falls back to generic centering.
    (int Left, int Top, int Right, int Bottom)? ITouchPlatform.QueryTopCutout()
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

    void ITouchPlatform.LeaveToHome() => RunOnUiThread(() => MoveTaskToBack(true));

    // ── APK assets ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// This build's <c>firebase-config.json</c>, bundled as an APK asset, or null when it has none.
    /// Absent is the normal state and costs nothing: the "Online game" entry does not appear and the
    /// app plays LAN, hot-seat and vs-computer games exactly as before.
    /// </summary>
    private string? TryLoadCloudConfig()
    {
        try
        {
            using var stream = Assets!.Open("firebase-config.json");
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd().Trim();
            return text.StartsWith('{') ? text : null;
        }
        catch
        {
            return null; // no such asset in this build
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
