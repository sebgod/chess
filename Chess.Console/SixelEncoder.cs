using System.Buffers;
using System.Runtime.InteropServices;
using ImageMagick;

namespace Chess.Console;

/// <summary>
/// Encodes a <see cref="MagickImage"/> (Q8) to Sixel terminal graphics format,
/// replacing the built-in <see cref="MagickFormat.Sixel"/> writer with a custom
/// implementation that supports partial-image encoding without clone/crop.
/// 
/// <code>
/// Method              Mean        Ratio vs Magick
/// MagickSixel_Full    127.3 ms    1.00
/// CustomSixel_Full    9.1 ms      0.07 (14× faster)
/// MagickSixel_Partial 127.9 ms    1.00
/// CustomSixel_Partial 1.6 ms      0.01 (79× faster)
/// </code>
/// </summary>
internal static class SixelEncoder
{
    private const int MaxColors = 256;

    /// <summary>
    /// Writes the full image as a Sixel stream.
    /// </summary>
    public static void Encode(MagickImage image, Stream output)
        => Encode(image, 0, image.Height, output);

    /// <summary>
    /// Writes a vertical slice of the image as a Sixel stream,
    /// avoiding the need to clone and crop for partial renders.
    /// </summary>
    public static void Encode(MagickImage image, int startY, uint height, Stream output)
    {
        var w = (int)image.Width;
        var h = (int)height;
        var channels = (int)image.ChannelCount;

        var pixels = image.GetPixelsUnsafe();
        var rawPixels = pixels.GetArea(0, startY, image.Width, height);
        ArgumentNullException.ThrowIfNull(rawPixels);

        var pixelCount = w * h;
        var indexMap = ArrayPool<byte>.Shared.Rent(pixelCount);
        var sixelGrid = ArrayPool<byte>.Shared.Rent(MaxColors * w);
        var paletteArr = ArrayPool<int>.Shared.Rent(MaxColors);
        var outputBuf = ArrayPool<byte>.Shared.Rent(65_536);

        try
        {
            var paletteSize = BuildPaletteAndIndexMap(rawPixels, pixelCount, channels, indexMap, paletteArr);

            var writer = new BufferedWriter(output, outputBuf);
            WriteHeader(ref writer, w, h);
            WritePalette(ref writer, paletteArr, paletteSize);
            WriteSixelData(ref writer, indexMap, sixelGrid, w, h, paletteSize);
            WriteTerminator(ref writer);
            writer.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(outputBuf);
            ArrayPool<int>.Shared.Return(paletteArr);
            ArrayPool<byte>.Shared.Return(sixelGrid);
            ArrayPool<byte>.Shared.Return(indexMap);
        }
    }

    /// <summary>
    /// Single-pass palette construction and pixel-to-index mapping.
    /// Colors are assigned registers in encounter order; when the palette is full,
    /// additional colors are mapped to their nearest existing register.
    /// </summary>
    private static int BuildPaletteAndIndexMap(
        byte[] rawPixels, int pixelCount, int channels,
        byte[] indexMap, int[] palette)
    {
        var colorToIndex = new Dictionary<int, byte>(capacity: 64);
        var paletteSize = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * channels;
            var packed = (rawPixels[offset] << 16) | (rawPixels[offset + 1] << 8) | rawPixels[offset + 2];

            ref var idx = ref CollectionsMarshal.GetValueRefOrAddDefault(colorToIndex, packed, out var exists);
            if (!exists)
            {
                if (paletteSize < MaxColors)
                {
                    idx = (byte)paletteSize;
                    palette[paletteSize] = packed;
                    paletteSize++;
                }
                else
                {
                    idx = FindNearest(palette, paletteSize, packed);
                }
            }
            indexMap[i] = idx;
        }

        return paletteSize;
    }

    private static byte FindNearest(int[] palette, int paletteSize, int packed)
    {
        var r = (packed >> 16) & 0xFF;
        var g = (packed >> 8) & 0xFF;
        var b = packed & 0xFF;

        var bestIdx = 0;
        var bestDist = int.MaxValue;

        for (var i = 0; i < paletteSize; i++)
        {
            var pr = (palette[i] >> 16) & 0xFF;
            var pg = (palette[i] >> 8) & 0xFF;
            var pb = palette[i] & 0xFF;

            var dist = (r - pr) * (r - pr) + (g - pg) * (g - pg) + (b - pb) * (b - pb);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }

        return (byte)bestIdx;
    }

    // DCS 0 ; 1 q  — macro param 0, background mode "no change"
    private static void WriteHeader(ref BufferedWriter w, int width, int height)
    {
        w.WriteByte(0x1B);              // ESC
        w.WriteByte((byte)'P');         // DCS introducer
        w.WriteAscii("0;1q"u8);
        w.WriteByte((byte)'"');         // raster attributes
        w.WriteInt(1);                  // Pan (aspect numerator)
        w.WriteByte((byte)';');
        w.WriteInt(1);                  // Pad (aspect denominator)
        w.WriteByte((byte)';');
        w.WriteInt(width);              // Ph (pixel width)
        w.WriteByte((byte)';');
        w.WriteInt(height);             // Pv (pixel height)
    }

    private static void WritePalette(ref BufferedWriter w, int[] palette, int paletteSize)
    {
        for (var i = 0; i < paletteSize; i++)
        {
            var packed = palette[i];
            w.WriteByte((byte)'#');
            w.WriteInt(i);
            w.WriteAscii(";2;"u8);
            w.WriteInt(((packed >> 16) & 0xFF) * 100 / 255);   // R %
            w.WriteByte((byte)';');
            w.WriteInt(((packed >> 8) & 0xFF) * 100 / 255);    // G %
            w.WriteByte((byte)';');
            w.WriteInt((packed & 0xFF) * 100 / 255);            // B %
        }
    }

    /// <summary>
    /// Precomputes sixel bits for all colors in each band in a single row-major pass,
    /// then RLE-encodes each present color from the contiguous sixelGrid slice.
    /// </summary>
    private static void WriteSixelData(
        ref BufferedWriter w, byte[] indexMap, byte[] sixelGrid,
        int width, int height, int paletteSize)
    {
        Span<bool> colorPresent = stackalloc bool[MaxColors];

        for (var band = 0; band < height; band += 6)
        {
            var bandH = Math.Min(6, height - band);

            // Clear only the portion we use
            sixelGrid.AsSpan(0, paletteSize * width).Clear();
            colorPresent[..paletteSize].Clear();

            // Single pass over the band: build sixel bits AND detect color presence
            for (var row = 0; row < bandH; row++)
            {
                var rowBit = (byte)(1 << row);
                var rowStart = (band + row) * width;
                for (var col = 0; col < width; col++)
                {
                    var ci = indexMap[rowStart + col];
                    sixelGrid[ci * width + col] |= rowBit;
                    colorPresent[ci] = true;
                }
            }

            // Encode each present color from its contiguous slice
            var firstColor = true;
            for (var ci = 0; ci < paletteSize; ci++)
            {
                if (!colorPresent[ci])
                {
                    continue;
                }

                if (!firstColor)
                {
                    w.WriteByte((byte)'$');  // CR — overlay next color in same band
                }
                firstColor = false;

                // Select color register
                w.WriteByte((byte)'#');
                w.WriteInt(ci);

                // RLE-encode from the contiguous sixel grid slice
                var colorSlice = sixelGrid.AsSpan(ci * width, width);
                byte prevChar = 0;
                var runLen = 0;

                for (var col = 0; col < width; col++)
                {
                    var ch = (byte)(colorSlice[col] + 0x3F);

                    if (ch == prevChar && runLen > 0)
                    {
                        runLen++;
                    }
                    else
                    {
                        FlushRun(ref w, prevChar, runLen);
                        prevChar = ch;
                        runLen = 1;
                    }
                }

                FlushRun(ref w, prevChar, runLen);
            }

            // LF — advance to next 6-pixel band (skip after the last band)
            if (band + 6 < height)
            {
                w.WriteByte((byte)'-');
            }
        }
    }

    private static void FlushRun(ref BufferedWriter w, byte ch, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count <= 3)
        {
            for (var i = 0; i < count; i++)
            {
                w.WriteByte(ch);
            }
        }
        else
        {
            w.WriteByte((byte)'!');
            w.WriteInt(count);
            w.WriteByte(ch);
        }
    }

    private static void WriteTerminator(ref BufferedWriter w)
    {
        w.WriteByte(0x1B);          // ESC
        w.WriteByte((byte)'\\');    // ST
    }

    /// <summary>
    /// Minimal buffered writer that batches small writes into a pooled buffer
    /// before flushing to the underlying <see cref="Stream"/>.
    /// </summary>
    private ref struct BufferedWriter(Stream output, byte[] buffer)
    {
        private readonly Stream _output = output;
        private readonly byte[] _buffer = buffer;
        private int _pos;

        public void WriteByte(byte b)
        {
            if (_pos >= _buffer.Length)
            {
                Flush();
            }
            _buffer[_pos++] = b;
        }

        public void WriteAscii(ReadOnlySpan<byte> data)
        {
            if (_pos + data.Length > _buffer.Length)
            {
                Flush();
            }

            if (data.Length > _buffer.Length)
            {
                _output.Write(data);
                return;
            }

            data.CopyTo(_buffer.AsSpan(_pos));
            _pos += data.Length;
        }

        public void WriteInt(int value)
        {
            Span<byte> digits = stackalloc byte[11];
            var len = 0;

            if (value == 0)
            {
                WriteByte((byte)'0');
                return;
            }

            if (value < 0)
            {
                WriteByte((byte)'-');
                value = -value;
            }

            while (value > 0)
            {
                digits[len++] = (byte)('0' + value % 10);
                value /= 10;
            }

            // Reverse the digits into the buffer
            for (var i = len - 1; i >= 0; i--)
            {
                WriteByte(digits[i]);
            }
        }

        public void Flush()
        {
            if (_pos > 0)
            {
                _output.Write(_buffer.AsSpan(0, _pos));
                _pos = 0;
            }
        }
    }
}
