using System.Text;
using DIR.Lib;

// Bakes the chess glyph set into .sdfg files (SdfGlyphDiskCache format) at build time, so
// Chess.Web starts with a fully pre-rasterized MSDF atlas: the browser fetches these into the
// WASM in-memory FS and SdfFontAtlas bulk-loads them — zero runtime rasterization.
//
// Pure managed, no GPU/display — runs on any CI runner in seconds. The .sdfg file names are
// FNV-1a hashes of the font bytes (path-independent), so baking here and loading in the browser
// against the same .ttf content resolves to the same file. manifest.txt lists the produced
// files (one per line) because the client can't guess content-hash names.
//
// usage: BakeSdfAtlas <outDir> <pieceFont.ttf> <labelFont.ttf>

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: BakeSdfAtlas <outDir> <pieceFont.ttf> <labelFont.ttf>");
    return 1;
}

var outDir = Path.GetFullPath(args[0]);
var pieceFont = Path.GetFullPath(args[1]);
var labelFont = Path.GetFullPath(args[2]);

foreach (var font in new[] { pieceFont, labelFont })
{
    if (!File.Exists(font))
    {
        Console.Error.WriteLine($"font not found: {font}");
        return 1;
    }
}

// The chess charset. Pieces (U+2654-U+265F) come from the piece font; everything GameUI's
// labels, status messages, keymap overlay, and setup markers draw comes from the label font:
// printable ASCII covers coordinates/digits/messages, plus U+2715 (setup cross) and
// U+21C4 (en-passant marker). The startup menu (DIR.Lib PixelMenuWidget) adds three more label
// glyphs: U+25B6 (▶ selection prefix) and U+265A/U+2654 (the ♚ Chess ♔ title kings — baked from
// the label font here, distinct atlas keys from the piece-font kings above). Whitespace needs no
// baking — the atlas derives its advance from the 'n' reference glyph, which ASCII includes.
var pieceRunes = Enumerable.Range(0x2654, 12).Select(cp => new Rune(cp));
var labelRunes = Enumerable.Range(0x20, 0x7F - 0x20).Select(cp => new Rune(cp))
    .Append(new Rune(0x2715))
    .Append(new Rune(0x21C4))
    .Append(new Rune(0x25B6))
    .Append(new Rune(0x2654))
    .Append(new Rune(0x265A));

Directory.CreateDirectory(outDir);
using var rasterizer = new ManagedFontRasterizer();
using (var cache = new SdfGlyphDiskCache(outDir, SdfFontAtlas.SdfRasterSize, SdfFontAtlas.SdfSpread))
{
    Bake(cache, pieceFont, pieceRunes, "pieces");
    Bake(cache, labelFont, labelRunes, "labels");
} // Dispose drains the writer thread and closes the .sdfg files

// Ship as .sdfg.bin: static-file servers (Kestrel's dev middleware included) 404 unknown
// extensions, while .bin maps to application/octet-stream everywhere. The client strips the
// .bin suffix when writing into the WASM FS so SdfGlyphDiskCache still finds {hash}.sdfg.
foreach (var f in Directory.GetFiles(outDir, "*.sdfg"))
    File.Move(f, f + ".bin", overwrite: true);

// Manifest: plain text, one produced file per line (content-hash names aren't guessable).
var files = Directory.GetFiles(outDir, "*.sdfg.bin").Select(Path.GetFileName).Order().ToArray();
File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), files!);
Console.WriteLine($"baked {files.Length} .sdfg file(s) -> {outDir}");
return 0;

void Bake(SdfGlyphDiskCache cache, string fontPath, IEnumerable<Rune> runes, string label)
{
    var seen = new HashSet<(uint Gid, string? Name)>();
    var baked = 0;
    var blank = 0;
    foreach (var rune in runes)
    {
        if (Rune.IsWhiteSpace(rune)) continue;
        var id = rasterizer.ResolveGlyphIdentity(fontPath, rune, charCode: -1, GlyphMapHint.Auto);
        if (!seen.Add((id.Gid, id.Type1Name))) continue; // several runes can share one glyph (.notdef etc.)
        var bitmap = id.Type1Name is not null
            ? rasterizer.RasterizeGlyphMtsdfByType1Name(fontPath, SdfFontAtlas.SdfRasterSize, id.Type1Name, SdfFontAtlas.SdfSpread)
            : rasterizer.RasterizeGlyphMtsdfByGid(fontPath, SdfFontAtlas.SdfRasterSize, id.Gid, SdfFontAtlas.SdfSpread);
        if (bitmap.Width == 0 || bitmap.Height == 0) { blank++; continue; }
        cache.AppendGlyph(fontPath, id.Gid, id.Type1Name, in bitmap);
        baked++;
    }
    Console.WriteLine($"{label}: {baked} glyph(s) baked, {blank} blank, from {Path.GetFileName(fontPath)}");
}
