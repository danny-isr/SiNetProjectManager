using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace SiNetProjectManager.Services.Stamping;

// ─── Models ────────────────────────────────────────────────────────────────

public sealed class PdfPageInfo
{
    public int Index { get; init; }
    public double WidthPt { get; init; }
    public double HeightPt { get; init; }
    public bool HasStamp { get; init; }

    public override string ToString()
        => $"{Index}: {WidthPt:F0}×{HeightPt:F0} pt (HasStamp={HasStamp})";
}

public enum StampPlacement
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public sealed class PdfStampInsertOptions
{
    /// <summary>
    /// Target page index (0-based). Ignored when <see cref="StampAllPages"/> is true.
    /// When null and StampAllPages is false, defaults to page 0.
    /// </summary>
    public int? PageIndex { get; set; }

    /// <summary>When true, the stamp is applied to every page in the document.</summary>
    public bool StampAllPages { get; set; }

    /// <summary>0-based page index inside the template PDF that contains the stamp artwork.</summary>
    public int TemplatePageIndex { get; set; }

    /// <summary>Anchor position for the stamp on the page.</summary>
    public StampPlacement Placement { get; set; } = StampPlacement.BottomRight;

    /// <summary>Horizontal distance from the page edge, in PDF points (1 pt = 1/72 in).</summary>
    public double MarginX { get; set; } = 20;

    /// <summary>Vertical distance from the page edge, in PDF points.</summary>
    public double MarginY { get; set; } = 20;

    /// <summary>Uniform scale factor. Null keeps the stamp at its native size.</summary>
    public double? Scale { get; set; }

    public bool ThrowIfStampAlreadyExists { get; set; }
    public bool OverwriteOutput { get; set; } = true;
}

public sealed class PdfStampInsertResult
{
    public bool Success { get; init; }
    public bool StampAlreadyExisted { get; init; }
    public string OutputPath { get; init; } = "";
    public int[] StampedPageIndices { get; init; } = [];
    public string Message { get; init; } = "";
}

// ─── Manager ───────────────────────────────────────────────────────────────

/// <summary>
/// Static utility for overlaying a stamp (from a template PDF) onto pages
/// of a target PDF.  Mirrors the API style of <see cref="DwfStampManager"/>.
/// <para>
/// Stamp detection uses a custom PDF Info dictionary key
/// (<c>/SiStampedPages</c>) that records which page indices have been stamped.
/// </para>
/// </summary>
public static class PdfStampManager
{
    /// <summary>
    /// Custom key written into the PDF Info dictionary to track which pages
    /// have been stamped.  Format: comma-separated 0-based page indices.
    /// </summary>
    private const string StampMetadataKey = "/SiStampedPages";

    // ── Query ──────────────────────────────────────────────────────────

    public static IReadOnlyList<PdfPageInfo> GetPages(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found.", pdfPath);

        using var stream = new MemoryStream(File.ReadAllBytes(pdfPath));
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        var stamped = ReadStampedPages(doc);
        var result = new List<PdfPageInfo>(doc.PageCount);

        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            result.Add(new PdfPageInfo
            {
                Index = i,
                WidthPt = page.Width.Point,
                HeightPt = page.Height.Point,
                HasStamp = stamped.Contains(i)
            });
        }

        return result;
    }

    public static bool HasStamp(string pdfPath, int pageIndex)
    {
        var page = GetPages(pdfPath).FirstOrDefault(p => p.Index == pageIndex)
            ?? throw new InvalidOperationException($"Page index '{pageIndex}' not found.");

        return page.HasStamp;
    }

    // ── Stamp insertion ────────────────────────────────────────────────

    public static PdfStampInsertResult AddStampFromTemplate(
        string sourcePdfPath,
        string templateStampPdfPath,
        string outputPdfPath,
        PdfStampInsertOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputFiles(sourcePdfPath, templateStampPdfPath, outputPdfPath);

        if (File.Exists(outputPdfPath) && !options.OverwriteOutput)
            throw new IOException($"Output file already exists: {outputPdfPath}");

        // Read source into memory so the file is never locked
        using var sourceStream = new MemoryStream(File.ReadAllBytes(sourcePdfPath));
        using var doc = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify);

        // Load stamp artwork from the template PDF.
        // XPdfForm must stay alive until doc.Save — PdfSharp references it lazily.
        var stampForm = XPdfForm.FromFile(templateStampPdfPath);

        if (options.TemplatePageIndex >= stampForm.PageCount)
        {
            throw new InvalidOperationException(
                $"Template page index {options.TemplatePageIndex} is out of range " +
                $"(template has {stampForm.PageCount} page(s)).");
        }

        stampForm.PageIndex = options.TemplatePageIndex;

        var stampedPages = ReadStampedPages(doc);
        var targetIndices = ResolveTargetPages(doc, options);

        // All targets already stamped — short-circuit
        if (targetIndices.TrueForAll(stampedPages.Contains))
        {
            if (options.ThrowIfStampAlreadyExists)
                throw new InvalidOperationException("All target pages already contain a stamp.");

            return new PdfStampInsertResult
            {
                Success = true,
                StampAlreadyExisted = true,
                OutputPath = outputPdfPath,
                StampedPageIndices = [.. targetIndices],
                Message = "All target pages already have a stamp. No changes were made."
            };
        }

        var newlyStamped = new List<int>();

        foreach (var idx in targetIndices)
        {
            if (stampedPages.Contains(idx))
                continue;

            var page = doc.Pages[idx];

            // Append = draw on top of existing page content
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var (x, y, w, h) = CalculateStampRect(page, stampForm, options);
            gfx.DrawImage(stampForm, x, y, w, h);

            stampedPages.Add(idx);
            newlyStamped.Add(idx);
        }

        WriteStampedPages(doc, stampedPages);
        doc.Save(outputPdfPath);

        return new PdfStampInsertResult
        {
            Success = true,
            StampAlreadyExisted = false,
            OutputPath = outputPdfPath,
            StampedPageIndices = [.. newlyStamped],
            Message = $"Stamp applied to {newlyStamped.Count} page(s)."
        };
    }

    // ── Private helpers ────────────────────────────────────────────────

    private static void ValidateInputFiles(
        string sourcePdfPath,
        string templateStampPdfPath,
        string outputPdfPath)
    {
        if (!File.Exists(sourcePdfPath))
            throw new FileNotFoundException("Source PDF not found.", sourcePdfPath);

        if (!File.Exists(templateStampPdfPath))
            throw new FileNotFoundException("Stamp template PDF not found.", templateStampPdfPath);

        if (string.Equals(
                Path.GetFullPath(templateStampPdfPath),
                Path.GetFullPath(outputPdfPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Template stamp file and output file must not be the same.");
        }
    }

    private static List<int> ResolveTargetPages(
        PdfDocument doc,
        PdfStampInsertOptions options)
    {
        if (options.StampAllPages)
            return Enumerable.Range(0, doc.PageCount).ToList();

        int idx = options.PageIndex ?? 0;

        if (idx < 0 || idx >= doc.PageCount)
        {
            throw new InvalidOperationException(
                $"Page index {idx} is out of range (0..{doc.PageCount - 1}).");
        }

        return [idx];
    }

    private static (double X, double Y, double Width, double Height) CalculateStampRect(
        PdfPage page,
        XPdfForm stamp,
        PdfStampInsertOptions options)
    {
        var (pageW, pageH) = GetVisualPageSize(page);
        double stampW = stamp.PointWidth;
        double stampH = stamp.PointHeight;

        if (options.Scale is > 0)
        {
            stampW *= options.Scale.Value;
            stampH *= options.Scale.Value;
        }

        double mx = options.MarginX;
        double my = options.MarginY;

        var (x, y) = options.Placement switch
        {
            StampPlacement.TopLeft      => (mx, my),
            StampPlacement.TopCenter    => ((pageW - stampW) / 2.0, my),
            StampPlacement.TopRight     => (pageW - stampW - mx, my),
            StampPlacement.CenterLeft   => (mx, (pageH - stampH) / 2.0),
            StampPlacement.Center       => ((pageW - stampW) / 2.0, (pageH - stampH) / 2.0),
            StampPlacement.CenterRight  => (pageW - stampW - mx, (pageH - stampH) / 2.0),
            StampPlacement.BottomLeft   => (mx, pageH - stampH - my),
            StampPlacement.BottomCenter => ((pageW - stampW) / 2.0, pageH - stampH - my),
            StampPlacement.BottomRight  => (pageW - stampW - mx, pageH - stampH - my),
            _                           => (pageW - stampW - mx, pageH - stampH - my)
        };

        return (x, y, stampW, stampH);
    }

    private static HashSet<int> ReadStampedPages(PdfDocument doc)
    {
        var result = new HashSet<int>();

        if (!doc.Info.Elements.ContainsKey(StampMetadataKey))
            return result;

        var value = doc.Info.Elements.GetString(StampMetadataKey);
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var part in value.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int idx))
                result.Add(idx);
        }

        return result;
    }

    private static void WriteStampedPages(PdfDocument doc, HashSet<int> pages)
    {
        doc.Info.Elements.SetString(StampMetadataKey, string.Join(",", pages.Order()));
    }

    // ── Programmatic stamp generation ──────────────────────────────────

    /// <summary>
    /// Generates a stamp with dynamic content (title, inspector name, date)
    /// and applies it directly onto the target PDF pages — no template file needed.
    /// </summary>
    public static PdfStampInsertResult GenerateAndApplyStamp(
        string sourcePdfPath,
        string outputPdfPath,
        PdfGeneratedStampOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(sourcePdfPath))
            throw new FileNotFoundException("Source PDF not found.", sourcePdfPath);

        if (File.Exists(outputPdfPath) && !options.OverwriteOutput)
            throw new IOException($"Output file already exists: {outputPdfPath}");

        using var sourceStream = new MemoryStream(File.ReadAllBytes(sourcePdfPath));
        using var doc = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify);

        var stampedPages = ReadStampedPages(doc);
        var targetIndices = options.StampAllPages
            ? Enumerable.Range(0, doc.PageCount).ToList()
            : new List<int> { options.PageIndex ?? 0 };

        targetIndices.RemoveAll(i => i < 0 || i >= doc.PageCount);
        if (targetIndices.Count == 0)
        {
            return new PdfStampInsertResult
            {
                Success = true,
                StampAlreadyExisted = false,
                OutputPath = outputPdfPath,
                StampedPageIndices = [],
                Message = "No valid target pages."
            };
        }

        if (targetIndices.TrueForAll(stampedPages.Contains))
        {
            if (options.ThrowIfStampAlreadyExists)
                throw new InvalidOperationException("All target pages already contain a stamp.");

            return new PdfStampInsertResult
            {
                Success = true,
                StampAlreadyExisted = true,
                OutputPath = outputPdfPath,
                StampedPageIndices = [.. targetIndices],
                Message = "All target pages already have a stamp."
            };
        }

        var newlyStamped = new List<int>();

        foreach (var idx in targetIndices)
        {
            if (stampedPages.Contains(idx)) continue;

            var page = doc.Pages[idx];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var (visualW, visualH) = GetVisualPageSize(page);
            DrawStampContent(gfx, visualW, visualH, options);

            stampedPages.Add(idx);
            newlyStamped.Add(idx);
        }

        WriteStampedPages(doc, stampedPages);
        doc.Save(outputPdfPath);

        return new PdfStampInsertResult
        {
            Success = true,
            StampAlreadyExisted = false,
            OutputPath = outputPdfPath,
            StampedPageIndices = [.. newlyStamped],
            Message = $"Stamp generated on {newlyStamped.Count} page(s)."
        };
    }

    private static void DrawStampContent(
        XGraphics gfx, double pageW, double pageH,
        PdfGeneratedStampOptions options)
    {
        double w = options.Width;
        double h = options.Height;

        var (x, y) = CalculatePosition(pageW, pageH, w, h,
            options.Placement, options.MarginX, options.MarginY);

        // Border only — no fill so content behind the stamp is visible.
        var borderPen = new XPen(XColor.FromArgb(255, 46, 125, 50), 1.5);
        gfx.DrawRectangle(borderPen, x, y, w, h);

        var greenBrush = new XSolidBrush(XColor.FromArgb(255, 46, 125, 50));

        // Title (centered)
        var titleFont = new XFont("Arial", 14, XFontStyleEx.Bold);
        gfx.DrawString(options.Title, titleFont, greenBrush,
            new XRect(x, y + 4, w, 22), XStringFormats.TopCenter);

        // Separator line
        var linePen = new XPen(XColor.FromArgb(255, 46, 125, 50), 0.5);
        gfx.DrawLine(linePen, x + 8, y + 26, x + w - 8, y + 26);

        // Inspector name (centered)
        var detailFont = new XFont("Arial", 9, XFontStyleEx.Regular);
        gfx.DrawString(options.InspectorName, detailFont, XBrushes.Black,
            new XRect(x, y + 30, w, 16), XStringFormats.Center);

        // Date (centered)
        var dateText = options.StampDate.ToString("dd/MM/yyyy");
        gfx.DrawString(dateText, detailFont, XBrushes.Black,
            new XRect(x, y + 48, w, 16), XStringFormats.Center);
    }

    private static (double X, double Y) CalculatePosition(
        double pageW, double pageH, double stampW, double stampH,
        StampPlacement placement, double mx, double my)
    {
        return placement switch
        {
            StampPlacement.TopLeft      => (mx, my),
            StampPlacement.TopCenter    => ((pageW - stampW) / 2.0, my),
            StampPlacement.TopRight     => (pageW - stampW - mx, my),
            StampPlacement.CenterLeft   => (mx, (pageH - stampH) / 2.0),
            StampPlacement.Center       => ((pageW - stampW) / 2.0, (pageH - stampH) / 2.0),
            StampPlacement.CenterRight  => (pageW - stampW - mx, (pageH - stampH) / 2.0),
            StampPlacement.BottomLeft   => (mx, pageH - stampH - my),
            StampPlacement.BottomCenter => ((pageW - stampW) / 2.0, pageH - stampH - my),
            StampPlacement.BottomRight  => (pageW - stampW - mx, pageH - stampH - my),
            _                           => (pageW - stampW - mx, pageH - stampH - my)
        };
    }

    /// <summary>
    /// Returns the visual page dimensions in points, accounting for the
    /// <c>/Rotate</c> entry. When a page is rotated 90° or 270°, the
    /// MediaBox width/height are swapped relative to what the viewer displays.
    /// PdfSharp’s <see cref="XGraphics"/> applies the rotation transform
    /// automatically, so drawing coordinates must use these visual dimensions.
    /// </summary>
    private static (double Width, double Height) GetVisualPageSize(PdfPage page)
    {
        bool swapped = page.Rotate is 90 or 270;
        return swapped
            ? (page.Height.Point, page.Width.Point)
            : (page.Width.Point, page.Height.Point);
    }
}

/// <summary>
/// Options for programmatic stamp generation (no template file needed).
/// Dynamic content is drawn directly onto each target page.
/// </summary>
public sealed class PdfGeneratedStampOptions
{
    /// <summary>Main stamp title (e.g., "מאושר").</summary>
    public string Title { get; set; } = "מאושר";

    /// <summary>Inspector/approver name.</summary>
    public string InspectorName { get; set; } = "";

    /// <summary>Date to display on the stamp.</summary>
    public DateTime StampDate { get; set; } = DateTime.Now;

    /// <summary>Target page index (0-based). Ignored when <see cref="StampAllPages"/> is true.</summary>
    public int? PageIndex { get; set; }

    /// <summary>When true, the stamp is applied to every page.</summary>
    public bool StampAllPages { get; set; }

    /// <summary>Anchor position for the stamp on the page.</summary>
    public StampPlacement Placement { get; set; } = StampPlacement.BottomRight;

    /// <summary>Stamp box width in PDF points.</summary>
    public double Width { get; set; } = 180;

    /// <summary>Stamp box height in PDF points.</summary>
    public double Height { get; set; } = 70;

    /// <summary>Horizontal margin from page edge in points.</summary>
    public double MarginX { get; set; } = 20;

    /// <summary>Vertical margin from page edge in points.</summary>
    public double MarginY { get; set; } = 20;

    public bool ThrowIfStampAlreadyExists { get; set; }
    public bool OverwriteOutput { get; set; } = true;
}
