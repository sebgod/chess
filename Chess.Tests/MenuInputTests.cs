using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// A press on a menu row confirms that row. Since DIR.Lib 10.0 a <see cref="PixelMenuWidget{TSurface}"/> takes
/// presses only through an <see cref="InputRouter"/>; every chess menu host passed them to the widget's own
/// HandleInput, which ignores them, so no menu took a click or a tap (found on the first iPad run: 42 taps on
/// the rows, nothing happened). The menu is the real startup wizard's, painted off-screen at an iPad's size.
/// </summary>
public sealed class MenuInputTests
{
    private static (PixelMenuWidget<RgbaImage> Menu, ClickableRegion[] Rows) PaintedStartupMenu(RgbaImageRenderer renderer)
    {
        var wizard = new StartupWizard();
        var menu = new PixelMenuWidget<RgbaImage>(renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = wizard.Current;
        menu.Reset(title, prompt, [.. items]);
        menu.Render();
        var rows = menu.GetRegisteredRegions().Where(r => r.OnClick is not null).ToArray();
        rows.Length.ShouldBe(items.Count(), "one clickable region per row");
        return (menu, rows);
    }

    private static InputEvent.MouseDown PressOn(ClickableRegion row) => new(row.X + row.Width / 2, row.Y + row.Height / 2);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_press_on_a_row_confirms_that_row(int row)
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, rows) = PaintedStartupMenu(renderer);

        MenuInput.Handle(menu, PressOn(rows[row])).ShouldBeTrue();

        menu.IsConfirmed.ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(row);
    }

    [Fact]
    public void A_press_beside_the_rows_confirms_nothing()
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, _) = PaintedStartupMenu(renderer);

        MenuInput.Handle(menu, new InputEvent.MouseDown(5, 5));

        menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void Keys_still_reach_the_widget()
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, _) = PaintedStartupMenu(renderer);

        MenuInput.Handle(menu, new InputEvent.KeyDown(InputKey.Down)).ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(1);
        MenuInput.Handle(menu, new InputEvent.KeyDown(InputKey.Enter)).ShouldBeTrue();
        menu.IsConfirmed.ShouldBeTrue();
    }

    [Fact]
    public void The_widget_alone_ignores_a_press_which_is_why_the_hosts_route_it()
    {
        using var renderer = new RgbaImageRenderer(2360, 1640);
        var (menu, rows) = PaintedStartupMenu(renderer);

        menu.HandleInput(PressOn(rows[1])).ShouldBeFalse();
        menu.IsConfirmed.ShouldBeFalse();
    }
}
