using Chess.Lib.UI;
using Chess.Touch;
using DIR.Lib;
using Foundation;
using SdlVulkan.Renderer;
using UIKit;

namespace Chess.iOS;

/// <summary>
/// The iPad chess head: <see cref="SdlVulkanIosApp"/> brings up SDL3 + Vulkan (MoltenVK) and calls
/// <see cref="OnRendererReady"/>, which hands the renderer to the shared touch game
/// (<see cref="TouchChessGame"/>, the same one Chess.Droid runs). What stays here is what only iOS
/// does (<see cref="ITouchPlatform"/>): a log the Files app can show, the name dialog, and where the
/// app may write.
/// </summary>
public sealed class ChessIosApp : SdlVulkanIosApp, ITouchPlatform
{
    protected override string WindowTitle => "Chess";

    // Match the display's own canvas background so the raw-cleared surface and the margins GameUI
    // paints don't band (same rationale as the desktop GUI and Chess.Droid).
    protected override RGBAColor32 BackgroundColor => PixelGameDisplay<VulkanContext>.Background;

    protected override void OnRendererReady(VkRenderer renderer, SdlEventLoop loop)
    {
        StartLog();
        foreach (var font in new[] { FontPaths.DejaVuSans, FontPaths.Merida })
        {
            if (!File.Exists(font))
                SdlEventLoop.DiagnosticLog?.Invoke($"[chess] the bundle has no font at {font}; the board will draw without it");
        }
        new TouchChessGame(this, TryLoadCloudConfig()).Attach(renderer, loop, SdlWindow);
    }

    // ── The log ─────────────────────────────────────────────────────────────────────────────────

    // Reading a device's console from Windows is awkward, so the renderer's diagnostics also go to
    // Documents/chess.log, which the Files app shows (UIFileSharingEnabled in Info.plist): a tester
    // opens it on the iPad and sends it. Replaced on every launch, so it holds the run being asked
    // about. stderr still gets every line, for a Mac with Console.app.
    private static void StartLog()
    {
        var stderr = SdlEventLoop.DiagnosticLog;
        StreamWriter? file = null;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "chess.log");
            file = new StreamWriter(path, append: false) { AutoFlush = true };
            file.WriteLine($"Chess {NSBundle.MainBundle.InfoDictionary["CFBundleShortVersionString"]} on {UIDevice.CurrentDevice.Model} iPadOS {UIDevice.CurrentDevice.SystemVersion}, started {DateTimeOffset.Now:O}");
        }
        catch (Exception ex)
        {
            stderr?.Invoke($"[chess] no log file: {ex.Message}");
        }

        SdlEventLoop.DiagnosticLog = m =>
        {
            stderr?.Invoke(m);
            try { file?.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} {m}"); } catch { /* a full disk must not end the game */ }
        };
    }

    // ── ITouchPlatform ──────────────────────────────────────────────────────────────────────────

    // Library/Application Support: private to the app and backed up, and NOT the Documents folder the
    // Files app shows, so the save and the profile cannot be deleted or edited by hand from there.
    string ITouchPlatform.DataDirectory
    {
        get
        {
            var urls = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User);
            var path = urls.Length > 0 && urls[0].Path is { } p
                ? p
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    // Since iOS 16 this is the model ("iPad") rather than the owner's name, unless the app holds an
    // entitlement a free signing account cannot have. Either way it is only the dialog's starting text.
    string ITouchPlatform.DefaultPlayerName => UIDevice.CurrentDevice.Name is { Length: > 0 } n ? n : "Player";

    void ITouchPlatform.PromptName(string title, string current, Action<string> join, Action cancel)
    {
        // SDL runs the game on the main thread on iOS, but a dialog is UIKit's, so be sure of it.
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var alert = UIAlertController.Create(title, null, UIAlertControllerStyle.Alert);
            alert.AddTextField(field =>
            {
                field.Text = current;
                field.ClearButtonMode = UITextFieldViewMode.WhileEditing;
                field.ReturnKeyType = UIReturnKeyType.Join;
            });
            alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => cancel()));
            alert.AddAction(UIAlertAction.Create("Join", UIAlertActionStyle.Default,
                _ => join(alert.TextFields is [{ Text: { } text }, ..] ? text : "")));

            if (TopViewController() is { } host)
                host.PresentViewController(alert, animated: true, completionHandler: null);
            else
            {
                SdlEventLoop.DiagnosticLog?.Invoke("[chess] no view controller to show the name dialog on; using the default name");
                join(current);
            }
        });
    }

    // SDL's window's root view controller, or whatever it is already presenting.
    private static UIViewController? TopViewController()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);
        var top = window?.RootViewController;
        while (top?.PresentedViewController is { } presented)
            top = presented;
        return top;
    }

    // Nothing to hold. Receiving UDP broadcast on iOS 14+ needs the multicast networking entitlement,
    // which Apple grants on request and a free signing account cannot carry, so a sideloaded build is
    // not expected to see LAN peers; the cloud lobby is the iPad's network game. See docs/ios.md.
    void ITouchPlatform.AcquireLanDiscovery() { }

    void ITouchPlatform.ReleaseLanDiscovery() { }

    // The safe-area insets already keep the strip clear of every iPad's camera; there is no punch-hole
    // to line text up with.
    (int Left, int Top, int Right, int Bottom)? ITouchPlatform.QueryTopCutout() => null;

    // iOS has no back gesture, and an app never sends itself home. Esc from an attached keyboard at
    // the menu simply does nothing.
    void ITouchPlatform.LeaveToHome() { }

    // ── The bundle ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// This build's <c>firebase-config.json</c>, bundled at the app's root, or null when it has none.
    /// Absent is the normal state and costs nothing: the "Online game" entry does not appear.
    /// </summary>
    private static string? TryLoadCloudConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "firebase-config.json");
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        return text.StartsWith('{') ? text : null;
    }
}
