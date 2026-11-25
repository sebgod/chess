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
/// does (<see cref="ITouchPlatform"/>): the name dialog, and where the app may write. The log the
/// Files app shows, Documents/chess.log, is the host's default (named after <see cref="WindowTitle"/>):
/// replaced on every launch, headed by the version, the device and the OS, and started before the
/// bring-up, so it also holds a failure there. stderr still gets every line, for a Mac with
/// Console.app.
/// </summary>
public sealed class ChessIosApp : SdlVulkanIosApp, ITouchPlatform
{
    protected override string WindowTitle => "Chess";

    // Match the display's own canvas background so the raw-cleared surface and the margins GameUI
    // paints don't band (same rationale as the desktop GUI and Chess.Droid).
    protected override RGBAColor32 BackgroundColor => PixelGameDisplay<VulkanContext>.Background;

    protected override void OnRendererReady(VkRenderer renderer, SdlEventLoop loop)
    {
        foreach (var font in new[] { FontPaths.DejaVuSans, FontPaths.Merida })
        {
            if (!File.Exists(font))
                SdlEventLoop.DiagnosticLog?.Invoke($"[chess] the bundle has no font at {font}; the board will draw without it");
        }
        new TouchChessGame(this).Attach(renderer, loop, SdlWindow);
        IosDiagnostics.Start(loop, renderer, SdlWindow);
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

    // Nothing to hold: iOS has no multicast lock to take. Apple documents its multicast networking
    // entitlement (which a free signing account cannot carry) as needed to receive UDP broadcast on iOS
    // 14+, yet a sideloaded build does see LAN peers (2026-11-15). See docs/ios.md.
    void ITouchPlatform.AcquireLanDiscovery() { }

    void ITouchPlatform.ReleaseLanDiscovery() { }

    // The safe-area insets already keep the strip clear of every iPad's camera; there is no punch-hole
    // to line text up with.
    (int Left, int Top, int Right, int Bottom)? ITouchPlatform.QueryTopCutout() => null;

    // iOS has no back gesture, and an app never sends itself home. Esc from an attached keyboard at
    // the menu simply does nothing.
    void ITouchPlatform.LeaveToHome() { }

    // ── The bundle ──────────────────────────────────────────────────────────────────────────────

    // A bundled file is a file in the app bundle here (AppContext.BaseDirectory is the bundle's root, where
    // Chess.iOS.csproj's BundleResource items land).
    Stream? ITouchPlatform.OpenBundled(string path)
    {
        var full = Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(full) ? File.OpenRead(full) : null;
    }
}
