using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;

namespace Chess.UI.Windows;

public sealed class FontCache() : IDisposable
{
    private record struct FontKey(FontFamily Family, int FontSize, GraphicsUnit Unit);

    private readonly PrivateFontCollection _fontCollection = new PrivateFontCollection();
    private readonly Dictionary<string, FontFamily> _loadedFamilies = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FontKey, Font> _cachedFonts = [];

    public void Dispose()
    {
        foreach (var familiy in _loadedFamilies.Values)
        {
            familiy.Dispose();
        }

        foreach (var font in _cachedFonts.Values)
        {
            font.Dispose();
        }

        _fontCollection.Dispose();
    }

    public FontFamily GetFontFamily(string fontFileOrFamily)
    {
        if (!_loadedFamilies.TryGetValue(fontFileOrFamily, out var fontFamily))
        {
            _fontCollection.AddFontFile(fontFileOrFamily);
            fontFamily = _fontCollection.Families[^1];

            _loadedFamilies.Add(fontFileOrFamily, fontFamily);
        }

        return fontFamily;
    }

    public void ClearCachedFonts()
    {
        var copy = new Dictionary<FontKey, Font>(_cachedFonts);
        foreach (var (key, value) in copy)
        {
            if (_cachedFonts.Remove(key))
            {
                value.Dispose();
            }
        }
    }

    public Font GetFont(FontFamily family, float fontSize, GraphicsUnit unit)
    {
        var key = new FontKey(family, (int)Math.Round(fontSize * 10), unit);

        if (!_cachedFonts.TryGetValue(key, out var font))
        {
            font = new Font(family, fontSize, unit);
            _cachedFonts[key] = font;
        }

        return font;
    }
}
