using Chess.Console;
using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;

var game = new Game();
using var image = new MagickImage(new MagickColor(0xff, 0xff, 0xff), 1000, 1000)
{
    Format = MagickFormat.Png
};

var ui = new ImageGameUI(game, image.Width, image.Height);

var clip = new RectInt((0, 0), (image.Width, image.Height));
ui.RenderUI(image, clip);
ui.RenderBoard(image, clip);

await image.WriteAsync("output.png");