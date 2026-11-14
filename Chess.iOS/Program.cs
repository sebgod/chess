namespace Chess.iOS;

public static class Program
{
    // SDL owns the UIKit application (SDL_RunApp runs UIApplicationMain with its own delegate), so
    // this calls the SDL host rather than UIApplication.Main, and does not return while the app runs.
    public static int Main(string[] args) => new ChessIosApp().Run();
}
