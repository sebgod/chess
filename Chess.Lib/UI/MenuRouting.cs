using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>
/// The <see cref="InputRouter"/> a host that shows a <see cref="PixelMenuWidget{TSurface}"/> owns for it. Since DIR.Lib
/// 10.0 the widget's own <see cref="PixelMenuWidget{TSurface}.HandleInput"/> takes keys only: a press on a row reaches
/// the row's click (<see cref="MenuModel.ConfirmAt"/>) through a router that walks the regions the widget registered
/// while painting. Chess's five menu hosts passed presses to <c>HandleInput</c>, so no menu took a click or a tap.
/// </summary>
/// <remarks>
/// The host builds ONE router when it builds its menu, with what it really has: the app's
/// <see cref="BackgroundTaskTracker"/> (polled each frame with <c>ProcessCompletions</c>, so work a click starts is
/// tracked and a fault is logged) and its own redraw request. Then every event the menu should see goes through
/// <see cref="InputRouter.Handle"/>: presses reach the rows, and whatever the router does not consume (the menu's
/// arrow keys and Enter) falls through to the widget via <see cref="InputRouter.Unhandled"/>.
/// </remarks>
public static class MenuRouting
{
    public static InputRouter For<TSurface>(
        PixelMenuWidget<TSurface> menu, BackgroundTaskTracker tasks, System.Action requestRedraw) =>
        new(menu.Ui, tasks, requestRedraw)
        {
            Widgets = () => [menu],
            Unhandled = menu.HandleInput,
        };
}
