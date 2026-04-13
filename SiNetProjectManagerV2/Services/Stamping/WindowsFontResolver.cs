using System;
using System.Collections.Concurrent;
using System.IO;
using PdfSharp.Fonts;

namespace SiNetProjectManagerV2.Services.Stamping;

/// <summary>
/// Resolves system fonts from the Windows Fonts directory for PdfSharp 6.x on .NET 8+.
/// PdfSharp cannot auto-discover system fonts in modern .NET — this resolver bridges the gap.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    private static readonly string FontsDir =
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    /// <summary>Cache of font file bytes to avoid repeated disk reads.</summary>
    private readonly ConcurrentDictionary<string, byte[]> _fontCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a font family + style to a face name (font file name).
    /// The returned <see cref="FontResolverInfo.FaceName"/> is passed to
    /// <see cref="GetFont"/> to retrieve the actual font bytes.
    /// </summary>
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        string fileName = MapToFileName(familyName, isBold, isItalic);
        string fullPath = Path.Combine(FontsDir, fileName);

        if (File.Exists(fullPath))
            return new FontResolverInfo(fileName);

        // Fallback: try base variant (simulate bold/italic if needed)
        string baseName = MapToFileName(familyName, false, false);
        string basePath = Path.Combine(FontsDir, baseName);

        if (File.Exists(basePath))
            return new FontResolverInfo(baseName, mustSimulateBold: isBold, mustSimulateItalic: isItalic);

        return null;
    }

    /// <summary>
    /// Returns the raw font bytes for the given face name (file name returned by
    /// <see cref="ResolveTypeface"/>).
    /// </summary>
    public byte[]? GetFont(string faceName)
    {
        return _fontCache.GetOrAdd(faceName, static key =>
        {
            string path = Path.Combine(FontsDir, key);
            return File.Exists(path) ? File.ReadAllBytes(path) : [];
        }) is { Length: > 0 } data ? data : null;
    }

    /// <summary>
    /// Maps a font family name and style flags to the expected Windows font file name.
    /// Covers the fonts used by the application (Arial, Tahoma, Times New Roman, etc.).
    /// </summary>
    private static string MapToFileName(string familyName, bool bold, bool italic)
    {
        return familyName.ToLowerInvariant() switch
        {
            "arial" => (bold, italic) switch
            {
                (false, false) => "arial.ttf",
                (true, false)  => "arialbd.ttf",
                (false, true)  => "ariali.ttf",
                (true, true)   => "arialbi.ttf"
            },
            "arial black" => "ariblk.ttf",
            "tahoma" => bold ? "tahomabd.ttf" : "tahoma.ttf",
            "times new roman" => (bold, italic) switch
            {
                (false, false) => "times.ttf",
                (true, false)  => "timesbd.ttf",
                (false, true)  => "timesi.ttf",
                (true, true)   => "timesbi.ttf"
            },
            "verdana" => (bold, italic) switch
            {
                (false, false) => "verdana.ttf",
                (true, false)  => "verdanab.ttf",
                (false, true)  => "verdanai.ttf",
                (true, true)   => "verdanaz.ttf"
            },
            "calibri" => (bold, italic) switch
            {
                (false, false) => "calibri.ttf",
                (true, false)  => "calibrib.ttf",
                (false, true)  => "calibrii.ttf",
                (true, true)   => "calibriz.ttf"
            },
            "segoe ui" => (bold, italic) switch
            {
                (false, false) => "segoeui.ttf",
                (true, false)  => "segoeuib.ttf",
                (false, true)  => "segoeuii.ttf",
                (true, true)   => "segoeuiz.ttf"
            },
            "david" => bold ? "davidbd.ttf" : "david.ttf",
            "miriam" => bold ? "mriamc.ttf" : "mriam.ttf",
            // Fallback: try familyName.ttf directly
            _ => $"{familyName.ToLowerInvariant().Replace(" ", "")}.ttf"
        };
    }
}
