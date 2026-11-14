namespace Chess.Touch;

/// <summary>
/// What a touch head's OS does differently, and nothing else: <see cref="TouchChessGame"/> is the
/// whole game on a touch screen, and calls through this for the few things only the OS can do.
/// Chess.Droid implements it on its activity, Chess.iOS on its SDL host.
/// </summary>
public interface ITouchPlatform
{
    /// <summary>App-private, writable storage: the save and the player's LAN profile live here.</summary>
    string DataDirectory { get; }

    /// <summary>The name a network game offers before the player has chosen one (the device's name).</summary>
    string DefaultPlayerName { get; }

    /// <summary>
    /// Asks for the player's display name in a native dialog, opened with <paramref name="current"/>.
    /// Exactly one of <paramref name="join"/> (with the text as typed, which may be blank or padded:
    /// the game trims it and falls back to a default name, so a platform need not) or
    /// <paramref name="cancel"/> is called, possibly on another thread, so both may only set flags
    /// the game picks up on its own thread.
    /// </summary>
    void PromptName(string title, string current, Action<string> join, Action cancel);

    /// <summary>
    /// Held while the LAN lobby browses: whatever the OS needs before an app receives UDP broadcast
    /// (Android's multicast lock). Best-effort; a platform that needs nothing does nothing.
    /// </summary>
    void AcquireLanDiscovery();

    /// <summary>Releases what <see cref="AcquireLanDiscovery"/> took. Safe to call when nothing is held.</summary>
    void ReleaseLanDiscovery();

    /// <summary>
    /// The camera punch-hole in DEVICE pixels, for the notch strip to line its text up with, or null
    /// where there is none or the OS does not say (the strip then centres in the safe-area inset).
    /// </summary>
    (int Left, int Top, int Right, int Bottom)? QueryTopCutout();

    /// <summary>
    /// Back pressed on the startup menu: hand the screen back to the OS (Android's launcher). A
    /// platform with no back gesture never gets here, and may do nothing.
    /// </summary>
    void LeaveToHome();
}
