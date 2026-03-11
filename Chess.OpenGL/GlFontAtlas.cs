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
    private const int MaxAtlasSize = 2048;

    private int _atlasWidth;
    private int _atlasHeight;
    private int _cursorX;
    private int _cursorY;
    private int _rowHeight;

    // Staging buffer for building the atlas before upload
    private MagickImage? _staging;

    // Dirty region tracking — bounding box of glyphs added since last Flush
    private int _dirtyX0;
    private int _dirtyY0;
    private int _dirtyX1;
    private int _dirtyY1;

    /// <summary>The OpenGL texture handle for the atlas.</summary>
    public uint TextureHandle => _textureHandle;

    public GlFontAtlas(GL gl, int initialWidth = 512, int initialHeight = 512)
    {
        _gl = gl;
        _atlasWidth = initialWidth;
        _atlasHeight = initialHeight;
        _staging = new MagickImage(MagickColors.Transparent, (uint)initialWidth, (uint)initialHeight);
        ResetDirtyRegion();

        _textureHandle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        // Allocate the GPU texture once with empty data
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            (uint)initialWidth, (uint)initialHeight, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Retrieves glyph info for the given character, rasterising it on demand if not yet cached.
    /// </summary>
    public GlyphInfo GetGlyph(string fontPath, float fontSize, char character)
    {
        // Quantize font size to reduce cache misses during window resize.
        // Without this, every pixel of resize produces new font sizes and
        // triggers expensive ImageMagick rasterisation for every glyph.
        fontSize = MathF.Round(fontSize);
        var key = new GlyphKey(fontPath, fontSize, character);
        if (_glyphs.TryGetValue(key, out var existing))
            return existing;

        return RasterizeGlyph(key);
    }

    /// <summary>
    /// Uploads any pending glyph rasterisations to the GPU texture.
    /// Only uploads the dirty region rather than the full atlas.
    /// Call this once per frame after all <see cref="GetGlyph"/> calls.
    /// </summary>
    public void Flush()
    {
        if (_dirtyX0 >= _dirtyX1 || _dirtyY0 >= _dirtyY1 || _staging is null)
            return;

        var regionW = _dirtyX1 - _dirtyX0;
        var regionH = _dirtyY1 - _dirtyY0;

        _gl.BindTexture(TextureTarget.Texture2D, _textureHandle);

        using var pixels = _staging.GetPixelsUnsafe();
        var rawPixels = pixels.GetArea(_dirtyX0, _dirtyY0, (uint)regionW, (uint)regionH);
        if (rawPixels is null) return;

        var channels = (int)_staging.ChannelCount;
        var pixelCount = regionW * regionH;
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

        _gl.TexSubImage2D<byte>(TextureTarget.Texture2D, 0,
            _dirtyX0, _dirtyY0, (uint)regionW, (uint)regionH,
            PixelFormat.Rgba, PixelType.UnsignedByte, rgba.AsSpan());

        ResetDirtyRegion();
    }

    /// <summary>
    /// Measures the pixel dimensions of the given text string.
    /// </summary>
    public (double Width, double Height) MeasureText(string fontPath, float fontSize, string text)
    {
        fontSize = MathF.Round(fontSize);
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

    private GlyphInfo RasterizeGlyph(GlyphKey key, bool retrying = false)
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

        // If we've exceeded vertical space, try to recover
        if (_cursorY + glyphHeight > _atlasHeight)
        {
            if (_atlasWidth < MaxAtlasSize || _atlasHeight < MaxAtlasSize)
            {
                // First attempt: grow the atlas (preserves existing glyphs & UVs)
                Grow();
                return RasterizeGlyph(key);
            }

            if (!retrying)
            {
                // At max size: evict stale glyphs and retry
                EvictAll();
                return RasterizeGlyph(key, retrying: true);
            }

            // Already at max size and evicted — give up
            return default;
        }

        // Composite into the staging atlas
        _staging?.Composite(glyphImage, _cursorX, _cursorY, CompositeOperator.Over);

        // Expand the dirty region to include this glyph
        _dirtyX0 = Math.Min(_dirtyX0, _cursorX);
        _dirtyY0 = Math.Min(_dirtyY0, _cursorY);
        _dirtyX1 = Math.Max(_dirtyX1, _cursorX + glyphWidth);
        _dirtyY1 = Math.Max(_dirtyY1, _cursorY + glyphHeight);

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

        return glyphInfo;
    }

    /// <summary>
    /// Doubles the atlas dimensions (up to <see cref="MaxAtlasSize"/>),
    /// preserving existing glyph data and rescaling cached UV coordinates.
    /// </summary>
    private void Grow()
    {
        var oldWidth = _atlasWidth;
        var oldHeight = _atlasHeight;

        _atlasWidth = Math.Min(_atlasWidth * 2, MaxAtlasSize);
        _atlasHeight = Math.Min(_atlasHeight * 2, MaxAtlasSize);

        // Preserve existing staging content in a larger buffer
        var newStaging = new MagickImage(MagickColors.Transparent, (uint)_atlasWidth, (uint)_atlasHeight);
        if (_staging is not null)
        {
            newStaging.Composite(_staging, 0, 0, CompositeOperator.Over);
            _staging.Dispose();
        }
        _staging = newStaging;

        // Rescale cached glyph UV coordinates for the new atlas dimensions
        var scaleX = (float)oldWidth / _atlasWidth;
        var scaleY = (float)oldHeight / _atlasHeight;
        var keys = new GlyphKey[_glyphs.Count];
        _glyphs.Keys.CopyTo(keys, 0);
        foreach (var key in keys)
        {
            var g = _glyphs[key];
            _glyphs[key] = g with
            {
                U0 = g.U0 * scaleX,
                V0 = g.V0 * scaleY,
                U1 = g.U1 * scaleX,
                V1 = g.V1 * scaleY
            };
        }

        // Reallocate the GPU texture at the new size
        _gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
        _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            (uint)_atlasWidth, (uint)_atlasHeight, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);

        // Mark entire atlas dirty so Flush uploads the preserved content
        _dirtyX0 = 0;
        _dirtyY0 = 0;
        _dirtyX1 = _atlasWidth;
        _dirtyY1 = _atlasHeight;
    }

    /// <summary>
    /// Clears all cached glyphs and resets the atlas packing cursors.
    /// The staging image is wiped to transparent so evicted glyphs
    /// do not bleed into newly rasterised ones.
    /// </summary>
    private void EvictAll()
    {
        _glyphs.Clear();
        _cursorX = 0;
        _cursorY = 0;
        _rowHeight = 0;

        // Wipe the staging buffer — a fresh transparent canvas
        _staging?.Dispose();
        _staging = new MagickImage(MagickColors.Transparent, (uint)_atlasWidth, (uint)_atlasHeight);

        // Mark the entire atlas dirty so the next Flush clears the GPU texture
        _dirtyX0 = 0;
        _dirtyY0 = 0;
        _dirtyX1 = _atlasWidth;
        _dirtyY1 = _atlasHeight;
    }

    private void ResetDirtyRegion()
    {
        _dirtyX0 = _atlasWidth;
        _dirtyY0 = _atlasHeight;
        _dirtyX1 = 0;
        _dirtyY1 = 0;
    }
}
