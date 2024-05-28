using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;

var game = new Game();
using var image = new MagickImage(MagickColors.White, 200 * 12, 200 * 12)
{
    Format = MagickFormat.Png,
    Density = new Density(90, DensityUnit.PixelsPerInch)
};

var imageRenderer = new MagickImageRenderer();
var ui = new GameUI(game, image.Width, image.Height);

var clip = new RectInt((image.Width, image.Height), (0, 0));
ui.Render(imageRenderer, image, clip);

await image.WriteAsync("output.png");