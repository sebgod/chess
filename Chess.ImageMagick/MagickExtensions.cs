using Console.Lib;
using ImageMagick;

namespace Chess.ImageMagick;

public static class MagickExtensions
{
    extension(MagickImage image)
    {
        /// <summary>
        /// Writes the full image as a Sixel stream.
        /// </summary>
        public void EncodeSixel(Stream output)
            => EncodeSixel(image, 0, image.Height, output);


        public void EncodeSixel(int startY, uint height, Stream output)
        {
            var channels = (int)image.ChannelCount;

            using var pixels = image.GetPixelsUnsafe();
            var rawPixels = pixels.GetArea(0, startY, image.Width, height) ?? throw new InvalidOperationException("Failed to get pixel data");

            SixelEncoder.Encode(rawPixels, (int)image.Width, (int)height, channels, output);
        }
    }
}
