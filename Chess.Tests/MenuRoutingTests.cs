using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// A press on a menu row confirms that row, through the router each host builds with its menu: it lists the menu and
/// has no fallback, because since DIR.Lib 11.69 the menu declares its own keys. Since DIR.Lib 10.0 a
/// <see cref="PixelMenuWidget{TSurface}"/> takes presses only through an <see cref="InputRouter"/>; every chess menu
/// host passed them to the widget's own HandleInput, which ignores them, so no menu took a click or a tap (found on
/// the first iPad run: 42 taps on the rows, nothing happened). The menu is the real startup wizard's, painted
/// off-screen at an iPad's size, as the full-screen menu and as the panel over the board (#84).
/// </summary>
public sealed class MenuRoutingTests
{
    // Toggled per test by the Theory's data; the panel is what the desktop draws since #84.
    private static void Paint(PixelMenuWidget<RgbaImage> menu, bool panel)
    {
        if (panel) menu.RenderPanel();
        else menu.Render();
    }

    private static (PixelMenuWidget<RgbaImage> Menu, InputRouter Router, ClickableRegion[] Rows) PaintedStartupMenu(
        RgbaImageRenderer renderer, StartupWizard wizard, bool panel = false)
    {
        var menu = new PixelMenuWidget<RgbaImage>(renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = wizard.Current;
        menu.Reset(title, prompt, [.. items]);
        Paint(menu, panel);
        var router = new InputRouter(menu.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [menu] };
        return (menu, router, Rows(menu, items.Length));
    }

    private static ClickableRegion[] Rows(PixelMenuWidget<RgbaImage> menu, int count)
    {
        var rows = menu.GetRegisteredRegions().Where(r => r.OnClick is not null).ToArray();
        rows.Length.ShouldBe(count, "one clickable region per row");
        return rows;
    }

    private static InputEvent.MouseDown PressOn(ClickableRegion row) => new(row.X + row.Width / 2, row.Y + row.Height / 2);

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void A_press_on_a_row_confirms_that_row(int row, bool panel)
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, router, rows) = PaintedStartupMenu(renderer, new StartupWizard(), panel);

        router.Handle(PressOn(rows[row])).ShouldBeTrue();

        menu.IsConfirmed.ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(row);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_press_beside_the_rows_confirms_nothing(bool panel)
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, router, _) = PaintedStartupMenu(renderer, new StartupWizard(), panel);

        router.Handle(new InputEvent.MouseDown(5, 5));

        menu.IsConfirmed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keys_step_the_menu_once(bool panel)
    {
        // Declared shortcuts: one Down is one row. A host that also wired the widget as the router's fallback would
        // step twice per key.
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, router, _) = PaintedStartupMenu(renderer, new StartupWizard(), panel);

        router.Handle(new InputEvent.KeyDown(InputKey.Down)).ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(1);
        router.Handle(new InputEvent.KeyDown(InputKey.Enter)).ShouldBeTrue();
        menu.IsConfirmed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void One_router_serves_every_wizard_step(bool panel)
    {
        // The hosts build the router once, with the menu, and Reset the menu for each wizard step: the router
        // must keep walking the regions of whatever the menu painted last.
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var wizard = new StartupWizard();
        var (menu, router, rows) = PaintedStartupMenu(renderer, wizard, panel);

        router.Handle(PressOn(rows[1])).ShouldBeTrue(); // Player vs Computer
        wizard.Confirm(menu.SelectedIndex);
        var (title, prompt, items) = wizard.Current;
        prompt.ShouldBe("Play as:");
        menu.Reset(title, prompt, [.. items]);
        Paint(menu, panel);

        router.Handle(PressOn(Rows(menu, items.Length)[1])).ShouldBeTrue(); // Black
        menu.IsConfirmed.ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void The_widget_alone_ignores_a_press_which_is_why_the_hosts_route_it()
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, _, rows) = PaintedStartupMenu(renderer, new StartupWizard());

        menu.HandleInput(PressOn(rows[1])).ShouldBeFalse();
        menu.IsConfirmed.ShouldBeFalse();
    }
}
