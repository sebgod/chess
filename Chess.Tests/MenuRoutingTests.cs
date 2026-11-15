using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// A press on a menu row confirms that row, through the host-owned router <see cref="MenuRouting"/> builds. Since
/// DIR.Lib 10.0 a <see cref="PixelMenuWidget{TSurface}"/> takes presses only through an <see cref="InputRouter"/>;
/// every chess menu host passed them to the widget's own HandleInput, which ignores them, so no menu took a click or a
/// tap (found on the first iPad run: 42 taps on the rows, nothing happened). The menu is the real startup wizard's,
/// painted off-screen at an iPad's size.
/// </summary>
public sealed class MenuRoutingTests
{
    private static (PixelMenuWidget<RgbaImage> Menu, InputRouter Router, ClickableRegion[] Rows) PaintedStartupMenu(
        RgbaImageRenderer renderer, StartupWizard wizard)
    {
        var menu = new PixelMenuWidget<RgbaImage>(renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = wizard.Current;
        menu.Reset(title, prompt, [.. items]);
        menu.Render();
        var router = MenuRouting.For(menu, new BackgroundTaskTracker(), static () => { });
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
    [InlineData(0)]
    [InlineData(2)]
    public void A_press_on_a_row_confirms_that_row(int row)
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, router, rows) = PaintedStartupMenu(renderer, new StartupWizard());

        router.Handle(PressOn(rows[row])).ShouldBeTrue();

        menu.IsConfirmed.ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(row);
    }

    [Fact]
    public void A_press_beside_the_rows_confirms_nothing()
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, router, _) = PaintedStartupMenu(renderer, new StartupWizard());

        router.Handle(new InputEvent.MouseDown(5, 5));

        menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void Keys_fall_through_to_the_menu()
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, router, _) = PaintedStartupMenu(renderer, new StartupWizard());

        router.Handle(new InputEvent.KeyDown(InputKey.Down)).ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(1);
        router.Handle(new InputEvent.KeyDown(InputKey.Enter)).ShouldBeTrue();
        menu.IsConfirmed.ShouldBeTrue();
    }

    [Fact]
    public void One_router_serves_every_wizard_step()
    {
        // The hosts build the router once, with the menu, and Reset the menu for each wizard step: the router
        // must keep walking the regions of whatever the menu painted last.
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var wizard = new StartupWizard();
        var (menu, router, rows) = PaintedStartupMenu(renderer, wizard);

        router.Handle(PressOn(rows[1])).ShouldBeTrue(); // Player vs Computer
        wizard.Confirm(menu.SelectedIndex);
        var (title, prompt, items) = wizard.Current;
        prompt.ShouldBe("Play as:");
        menu.Reset(title, prompt, [.. items]);
        menu.Render();

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
