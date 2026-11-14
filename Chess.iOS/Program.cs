using ObjCRuntime;

namespace Chess.iOS;

public static class Program
{
    // SDL owns the UIKit application (SDL_RunApp runs UIApplicationMain with its own delegate), so
    // this calls the SDL host rather than UIApplication.Main, and does not return while the app runs.
    public static int Main(string[] args)
    {
        CrashLog.Install();
        return new ChessIosApp().Run();
    }
}

/// <summary>
/// The trace of a crash that happens before chess.log exists. chess.log is opened in OnRendererReady,
/// once SDL and Vulkan are up, so a failure in the bring-up itself (or in the native-to-managed call
/// SDL makes to start it) ends the app with nothing in Files. These hooks are installed first thing in
/// Main and append the exception to Documents/chess-crash.log, which the Files app shows next to
/// chess.log. Two events, because on iOS a managed exception that reaches a native frame is normally
/// turned into an Objective-C exception (Runtime.MarshalManagedException fires, then the app aborts)
/// and never reaches AppDomain.UnhandledException. The same idea as pilot's gui.log (pilot #420).
/// </summary>
internal static class CrashLog
{
    private static string? _path;

    internal static void Install()
    {
        try
        {
            _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "chess-crash.log");
        }
        catch
        {
            // No Documents folder: stderr (the device log) is all there is.
        }

        Runtime.MarshalManagedException += (_, e) =>
            Write($"managed exception crossing into native code (mode {e.ExceptionMode})", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write($"unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject);
    }

    private static void Write(string what, object? exception)
    {
        var text = $"{DateTimeOffset.Now:O} {what}:{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
        Console.Error.Write("[chess] " + text);
        if (_path is null)
            return;
        try
        {
            File.AppendAllText(_path, text);
        }
        catch
        {
            // A crash handler must not throw; stderr already has it.
        }
    }
}
