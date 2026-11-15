using System.Diagnostics;
using System.Text;
using DIR.Lib;
using SdlVulkan.Renderer;
using UIKit;

namespace Chess.iOS;

/// <summary>
/// What chess.log needs to say when the iPad shows a picture but does not respond. The first run that
/// got that far (2026-11-15) drew the menu and then took no touch, key or trackpad input, and chess.log
/// held nothing past its header. These hooks tell the cases apart: the loop stopped (iterations do
/// not move), the loop runs but no input reaches it (iterations move, input counts do not), or input
/// arrives and the game ignores it (input counts move). The main-thread probe says whether the iOS run
/// loop is being run at all (SDL's pump runs it in slices; a block posted to the main thread only runs
/// inside one), and lists the windows on screen, which shows a launch window left on top of the game.
/// Counting only: every handler is wrapped and still called, and a handler the game did not set stays
/// unset.
/// </summary>
internal static class IosDiagnostics
{
    private static long _iterations, _frames, _pointer, _mouseDown, _mouseUp, _keyDown, _text;
    private static int _probePending, _pointerLogged;

    internal static void Start(SdlEventLoop loop, VkRenderer renderer, SdlVulkanWindow window)
    {
        Log($"[ios-diag] renderer {renderer.Width}x{renderer.Height} px, window pixel density {window.PixelDensity}");
        var iteration = loop.OnLoopIteration;
        loop.OnLoopIteration = () => { Interlocked.Increment(ref _iterations); iteration?.Invoke(); };
        var postFrame = loop.OnPostFrame;
        loop.OnPostFrame = () => { Interlocked.Increment(ref _frames); postFrame?.Invoke(); };
        if (loop.OnPointerInput is { } pointer)
            loop.OnPointerInput = e => { Interlocked.Increment(ref _pointer); return pointer(e); };
        else
            // The game sets none, so this one only looks: it logs where each press and release lands (in the pixels
            // the game hit-tests in, after the renderer's scaling) and never consumes. The first 60 are enough.
            loop.OnPointerInput = e =>
            {
                if (e is InputEvent.MouseDown or InputEvent.MouseUp && Interlocked.Increment(ref _pointerLogged) <= 60)
                    Log($"[ios-diag] {e} (renderer {renderer.Width}x{renderer.Height})");
                return false;
            };
        var mouseUp = loop.OnMouseUp;
        loop.OnMouseUp = b => { Interlocked.Increment(ref _mouseUp); mouseUp?.Invoke(b); };
        if (loop.OnMouseDown is { } mouseDown)
            loop.OnMouseDown = (b, x, y, c, m) => { Interlocked.Increment(ref _mouseDown); return mouseDown(b, x, y, c, m); };
        if (loop.OnKeyDown is { } keyDown)
            loop.OnKeyDown = k => { Interlocked.Increment(ref _keyDown); return keyDown(k); };
        if (loop.OnTextInput is { } text)
            loop.OnTextInput = s => { Interlocked.Increment(ref _text); text(s); };

        Log($"[ios-diag] game handlers: pointer={loop.OnPointerInput is not null} mouseDown={loop.OnMouseDown is not null} "
            + $"keyDown={loop.OnKeyDown is not null} textInput={loop.OnTextInput is not null}");
        new Thread(Heartbeat) { IsBackground = true, Name = "chess ios-diag" }.Start();
    }

    private static void Heartbeat()
    {
        var clock = Stopwatch.StartNew();
        var poster = new Foundation.NSObject();
        for (var beat = 0; ; beat++)
        {
            Thread.Sleep(beat < 30 ? 2000 : 10000); // every 2 s for the first minute, then every 10 s
            try
            {
                Log($"[ios-diag] t={clock.Elapsed.TotalSeconds:F0}s iterations={Interlocked.Read(ref _iterations)} "
                    + $"frames={Interlocked.Read(ref _frames)} pointer={Interlocked.Read(ref _pointer)} "
                    + $"mouseDown={Interlocked.Read(ref _mouseDown)} mouseUp={Interlocked.Read(ref _mouseUp)} keyDown={Interlocked.Read(ref _keyDown)} "
                    + $"text={Interlocked.Read(ref _text)} mainThreadProbeOutstanding={Volatile.Read(ref _probePending) == 1}");

                if (Interlocked.Exchange(ref _probePending, 1) == 0)
                {
                    var sent = clock.Elapsed;
                    poster.BeginInvokeOnMainThread(() =>
                    {
                        try { Log($"[ios-diag] main thread answered in {(clock.Elapsed - sent).TotalMilliseconds:F0} ms: {DescribeWindows()}"); }
                        catch (Exception ex) { Log($"[ios-diag] window probe failed: {ex.GetType().Name}: {ex.Message}"); }
                        finally { Volatile.Write(ref _probePending, 0); }
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"[ios-diag] heartbeat failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // Main thread only.
    private static string DescribeWindows()
    {
        var text = new StringBuilder();
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>())
        {
            text.Append($"scene {scene.ActivationState}");
            if (OperatingSystem.IsIOSVersionAtLeast(16))
                text.Append($" orientation={scene.EffectiveGeometry.InterfaceOrientation}");
            foreach (var window in scene.Windows)
            {
                text.Append($" | {window.Class.Name} root={window.RootViewController?.Class.Name ?? "-"} level={window.WindowLevel} "
                    + $"hidden={window.Hidden} alpha={window.Alpha:F2} key={window.IsKeyWindow} "
                    + $"interaction={window.UserInteractionEnabled} frame={window.Frame}");
            }
        }
        return text.Length > 0 ? text.ToString() : "no window scene";
    }

    private static void Log(string message) => SdlEventLoop.DiagnosticLog?.Invoke(message);
}
