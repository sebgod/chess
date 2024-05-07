using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;

var game = new Game();
using var image = new MagickImage(new MagickColor(0xff, 0xff, 0xff), 200 * 12, 200 * 12)
{
    Format = MagickFormat.Png
};

var ui = new ImageGameUI(game, image.Width, image.Height);

var clip = new RectInt((image.Width, image.Height), (0, 0));
ui.RenderUI(image, clip);
ui.RenderBoard(image, clip);

await image.WriteAsync("output.png");