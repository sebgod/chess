using DIR.Lib;

namespace Console.Lib;

/// <summary>
/// A renderer that can encode its surface as Sixel graphics.
/// </summary>
public abstract class SixelRenderer<TSurface>(TSurface surface) : Renderer<TSurface>(surface)
{
    public abstract void EncodeSixel(Stream output);
    public abstract void EncodeSixel(int startY, uint height, Stream output);
}
