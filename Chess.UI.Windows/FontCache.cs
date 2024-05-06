using System.Drawing.Text;

namespace Chess.UI.Windows;

public sealed class FontCache() : IDisposable
{
    private readonly PrivateFontCollection _fontCollection = new PrivateFontCollection();
    private readonly Dictionary<string, FontFamily> _loadedFamilies = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var familiy in _loadedFamilies.Values)
        {
            familiy.Dispose();
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
}
