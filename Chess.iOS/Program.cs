using ObjCRuntime;
using SharpAstro.AppShell;

namespace Chess.iOS;

public static class Program
{
    // SDL owns the UIKit application (SDL_RunApp runs UIApplicationMain with its own delegate), so
    // this calls the SDL host rather than UIApplication.Main, and does not return while the app runs.
    public static int Main(string[] args)
    {
        InstallCrashTrace();
        return new ChessIosApp().Run();
    }

    /// <summary>
    /// The trace of a crash that happens before chess.log exists. chess.log is opened in OnRendererReady,
    /// once SDL and Vulkan are up, so a failure in the bring-up itself (or in the native-to-managed call SDL
    /// makes to start it) ends the app with nothing in Files. AppShell's CrashTrace appends every uncaught
    /// exception to Documents/chess-crash.log, which the Files app shows next to chess.log (and stderr, the
    /// device log, when the file cannot be written). The one hook it cannot see is iOS's own: a managed
    /// exception that reaches a native frame is turned into an Objective-C exception and raises
    /// Runtime.MarshalManagedException, never AppDomain.UnhandledException. The first iPad crash went that way,
    /// so it is forwarded here.
    /// </summary>
    private static void InstallCrashTrace()
    {
        string? path = null;
        try
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "chess-crash.log");
        }
        catch
        {
            // No Documents folder: CrashTrace falls back to stderr.
        }

        if (path is not null)
            CrashTrace.Install(text => File.AppendAllText(path, text + Environment.NewLine + Environment.NewLine));
        else
            CrashTrace.Install(text => Console.Error.WriteLine("[chess] " + text));

        Runtime.MarshalManagedException += (_, e) =>
            CrashTrace.Write($"managed exception crossing into native code (mode {e.ExceptionMode})", e.Exception);
    }
}
