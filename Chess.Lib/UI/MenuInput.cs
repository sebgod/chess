using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>
/// Feeds a <see cref="PixelMenuWidget{TSurface}"/> both kinds of input. Since DIR.Lib 10.0 the widget's own
/// <see cref="PixelMenuWidget{TSurface}.HandleInput"/> takes KEYS only: a press on a row reaches the row's
/// click (<see cref="MenuModel.ConfirmAt"/>, select and confirm) through an <see cref="InputRouter"/> that walks
/// the regions the widget registered while painting. Every chess menu host kept calling
/// <c>HandleInput(MouseDown)</c>, which has answered false ever since, so no menu took a click or a tap: the
/// desktop's only through the keyboard, the touch game's (Android, iPad) not at all. Found on the first iPad
/// run, where 42 taps landed on the menu's rows and none did anything.
/// </summary>
/// <remarks>
/// A press goes through a router built for that press, over this one widget. It holds no hover or capture
/// state worth keeping (a menu row only answers a press), and a router per press cannot go stale when a host
/// rebuilds its menu. The regions it walks are the ones from the last frame the widget painted, which is the
/// frame the user pressed on. Everything else (the keys) goes to the widget as before.
/// </remarks>
public static class MenuInput
{
    /// <summary>Routes <paramref name="evt"/> to <paramref name="menu"/>. True when the menu consumed it; then
    /// <see cref="PixelMenuWidget{TSurface}.IsConfirmed"/> says whether that confirmed a row.</summary>
    public static bool Handle<TSurface>(PixelMenuWidget<TSurface> menu, InputEvent evt) => evt switch
    {
        InputEvent.MouseDown => new InputRouter(menu.Ui, new BackgroundTaskTracker(), static () => { })
        {
            Widgets = () => [menu],
        }.Handle(evt),
        _ => menu.HandleInput(evt),
    };
}
