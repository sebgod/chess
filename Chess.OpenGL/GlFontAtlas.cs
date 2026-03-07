using ImageMagick;
using Silk.NET.OpenGL;

namespace Chess.OpenGL;

/// <summary>
/// A cached glyph atlas texture for OpenGL text rendering.
/// Rasterises requested glyphs using ImageMagick into a single GPU texture,
/// storing UV coordinates for each (font, size, character) combination.
/// </summary>
internal sealed class GlFontAtlas : IDisposable
{
    private readonly record struct GlyphKey(string Font, float Size, char Character);

    /// <summary>UV coordinates and pixel dimensions of a single cached glyph.</summary>
    internal readonly record struct GlyphInfo(float U0, float V0, float U1, float V1, int Width, int Height, float AdvanceX);

    private readonly GL _gl;
    private readonly Dictionary<GlyphKey, GlyphInfo> _glyphs = new();

    private uint _textureHandle;
    private readonly int _atlasWidth;
    private readonly int _atlasHeight;
    private int _cursorX;
    private int _cursorY;
    private int _rowHeight;

    // Staging buffer for building the atlas before upload
    private MagickImage? _staging;
    private bool _dirty;

    /// <summary>The OpenGL texture handle for the atlas.</summary>
    public uint TextureHandle => _textureHandle;

    public GlFontAtlas(GL gl, int initialWidth = 2048, int initialHeight = 2048)
    {
        _gl = gl;
        _atlasWidth = initialWidth;
        _atlasHeight = initialHeight;
        _staging = new MagickImage(MagickColors.Transparent, (uint)initialWidth, (uint)initialHeight);

        _textureHandle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    /// <summary>
    /// Retrieves glyph info for the given character, rasterising it on demand if not yet cached.
    /// </summary>
    public GlyphInfo GetGlyph(string fontPath, float fontSize, char character)
    {
        var key = new GlyphKey(fontPath, fontSize, character);
        if (_glyphs.TryGetValue(key, out var existing))
            return existing;

        return RasterizeGlyph(key);
    }

    /// <summary>
    /// Uploads any pending glyph rasterisations to the GPU texture.
    /// Call this once per frame after all <see cref="GetGlyph"/> calls.
    /// </summary>
    public unsafe void Flush()
    {
        if (!_dirty || _staging is null)
            return;

        _gl.BindTexture(TextureTarget.Texture2D, _textureHandle);

        using var pixels = _staging.GetPixelsUnsafe();
        var rawPixels = pixels.GetArea(0, 0, _staging.Width, _staging.Height);
        if (rawPixels is null) return;

        // Magick.NET Q8 returns RGB by default; we need to convert to RGBA
        var channels = (int)_staging.ChannelCount;
        var pixelCount = _atlasWidth * _atlasHeight;
        var rgba = new byte[pixelCount * 4];

        for (var i = 0; i < pixelCount; i++)
        {
            var srcOffset = i * channels;
            var dstOffset = i * 4;

            if (channels >= 3)
            {
                rgba[dstOffset] = rawPixels[srcOffset];
                rgba[dstOffset + 1] = rawPixels[srcOffset + 1];
                rgba[dstOffset + 2] = rawPixels[srcOffset + 2];
                rgba[dstOffset + 3] = channels >= 4 ? rawPixels[srcOffset + 3] : (byte)255;
            }
        }

        fixed (byte* ptr = rgba)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)_atlasWidth, (uint)_atlasHeight, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }

        _dirty = false;
    }

    /// <summary>
    /// Measures the pixel dimensions of the given text string.
    /// </summary>
    public (double Width, double Height) MeasureText(string fontPath, float fontSize, string text)
    {
        var settings = new MagickReadSettings
        {
            Font = fontPath,
            FontPointsize = fontSize,
            BackgroundColor = MagickColors.Transparent,
            FillColor = MagickColors.White
        };

        using var label = new MagickImage($"label:{text}", settings);
        return (label.Width, label.Height);
    }

    public void Dispose()
    {
        if (_textureHandle != IntPtr.Zero)
        {
            _gl.DeleteTexture(_textureHandle);
            _textureHandle = 0;
        }

        _staging?.Dispose();
        _staging = null;
    }

    private GlyphInfo RasterizeGlyph(GlyphKey key)
    {
        // Spaces and whitespace: measure advance via a reference character, no texture needed
        if (char.IsWhiteSpace(key.Character))
        {
            var refGlyph = GetGlyph(key.Font, key.Size, 'n');
            var info = new GlyphInfo(0, 0, 0, 0, 0, 0, refGlyph.AdvanceX);
            _glyphs[key] = info;
            return info;
        }

        var settings = new MagickReadSettings
        {
            Font = key.Font,
            FontPointsize = key.Size,
            BackgroundColor = MagickColors.Transparent,
            FillColor = MagickColors.White
        };

        // Render the character into a small image
        using var glyphImage = new MagickImage($"label:{key.Character}", settings);

        var glyphWidth = (int)glyphImage.Width;
        var glyphHeight = (int)glyphImage.Height;

        if (glyphWidth == 0 || glyphHeight == 0)
            return default;

        // Check if we need to advance to the next row
        if (_cursorX + glyphWidth > _atlasWidth)
        {
            _cursorX = 0;
            _cursorY += _rowHeight + 1;
            _rowHeight = 0;
        }

        // If we've exceeded vertical space, the atlas is too small
        if (_cursorY + glyphHeight > _atlasHeight)
            return default;

        // Composite into the staging atlas
        _staging?.Composite(glyphImage, _cursorX, _cursorY, CompositeOperator.Over);

        var glyphInfo = new GlyphInfo(
            U0: _cursorX / (float)_atlasWidth,
            V0: _cursorY / (float)_atlasHeight,
            U1: (_cursorX + glyphWidth) / (float)_atlasWidth,
            V1: (_cursorY + glyphHeight) / (float)_atlasHeight,
            Width: glyphWidth,
            Height: glyphHeight,
            AdvanceX: glyphWidth
        );

        _glyphs[key] = glyphInfo;
        _cursorX += glyphWidth + 1;
        _rowHeight = Math.Max(_rowHeight, glyphHeight);
        _dirty = true;

        return glyphInfo;
    }
}
