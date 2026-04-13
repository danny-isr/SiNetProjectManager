using System.IO;
using SiNetSQL.Models;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services.Stamping;

/// <summary>
/// Orchestrates stamp operations on DWF/PDF drawing files.
/// Implements <see cref="IDrawingStampService"/> so the ViewModel layer
/// can trigger stamping without knowing about DWF/PDF internals.
/// </summary>
public sealed class DrawingStampService : IDrawingStampService
{
    public Task<IReadOnlyList<DrawingLayoutInfo>> GetLayoutsAsync(
        string filePath,
        DrawingFileType fileType,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DrawingLayoutInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                return Array.Empty<DrawingLayoutInfo>();

            if (fileType == DrawingFileType.Dwf)
            {
                var layouts = DwfStampManager.GetLayouts(filePath);
                return layouts.Select(l =>
                    new DrawingLayoutInfo(l.Index, l.LayoutName, l.HasStamp))
                    .ToList();
            }
            else
            {
                var pages = PdfStampManager.GetPages(filePath);
                return pages.Select(p =>
                    new DrawingLayoutInfo(p.Index, $"עמוד {p.Index + 1}", p.HasStamp))
                    .ToList();
            }
        }, cancellationToken);
    }

    public async Task<DrawingStampBatchResult> StampDrawingsAsync(
        IReadOnlyList<InspectionReportDrawing> drawings,
        StampContentInfo stampContent,
        string outputDirectory,
        string? dwfTemplatePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drawings);
        ArgumentNullException.ThrowIfNull(stampContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        var results = new List<DrawingStampItemResult>();

        foreach (var drawing in drawings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await Task.Run(
                () => StampSingleDrawing(drawing, stampContent, outputDirectory, dwfTemplatePath),
                cancellationToken);
            results.Add(result);
        }

        return new DrawingStampBatchResult(results);
    }

    private static DrawingStampItemResult StampSingleDrawing(
        InspectionReportDrawing drawing,
        StampContentInfo stampContent,
        string outputDirectory,
        string? dwfTemplatePath)
    {
        try
        {
            if (!File.Exists(drawing.SourceFilePath))
                return new DrawingStampItemResult(drawing.Id, false,
                    $"קובץ מקור לא נמצא: {drawing.SourceFilePath}");

            var extension = drawing.FileType == DrawingFileType.Dwf ? ".dwf" : ".pdf";
            var stampedFileName = $"{Path.GetFileNameWithoutExtension(drawing.FileName)}_stamped{extension}";
            var stampedPath = Path.Combine(outputDirectory, stampedFileName);

            var selectedIndices = ParseLayoutIndices(drawing.SelectedLayoutIndices);

            if (drawing.FileType == DrawingFileType.Dwf)
            {
                if (string.IsNullOrWhiteSpace(dwfTemplatePath) || !File.Exists(dwfTemplatePath))
                    return new DrawingStampItemResult(drawing.Id, false,
                        "דילוג על קובץ DWF – לא הוגדר נתיב תבנית חותמת DWF.");

                StampDwfFile(drawing.SourceFilePath, dwfTemplatePath, stampedPath, selectedIndices, stampContent);
            }
            else
            {
                StampPdfFile(drawing.SourceFilePath, stampContent, stampedPath, selectedIndices);
            }

            drawing.StampedFilePath = stampedPath;
            drawing.StampStatus = DrawingStampStatus.Stamped;
            drawing.StampedAt = DateTime.UtcNow;

            return new DrawingStampItemResult(drawing.Id, true, StampedFilePath: stampedPath);
        }
        catch (Exception ex)
        {
            drawing.StampStatus = DrawingStampStatus.Failed;
            return new DrawingStampItemResult(drawing.Id, false, ex.Message);
        }
    }

    private static void StampDwfFile(
        string sourcePath, string dwfTemplatePath, string outputPath,
        int[]? selectedIndices, StampContentInfo stampContent)
    {
        var layouts = DwfStampManager.GetLayouts(sourcePath);
        if (layouts.Count == 0) return;

        bool anyStamped = false;

        foreach (var layout in layouts)
        {
            if (selectedIndices is { Length: > 0 } && !selectedIndices.Contains(layout.Index))
                continue;

            if (layout.HasStamp)
                continue;

            var source = anyStamped ? outputPath : sourcePath;
            DwfStampManager.AddStampFromTemplate(source, dwfTemplatePath, outputPath,
                new DwfStampInsertOptions
                {
                    LayoutIndex = layout.Index,
                    OverwriteOutput = true
                });
            anyStamped = true;
        }

        if (!anyStamped)
        {
            File.Copy(sourcePath, outputPath, overwrite: true);
            return;
        }

        // Replace date placeholder in the stamp markup with the actual inspection date.
        // Template must contain "DD/MM/YYYY" (10 chars) → replaced with "01/04/2025" (10 chars).
        var replacements = new Dictionary<string, string>
        {
            [DwfStampManager.StampPlaceholders.Date] = stampContent.StampDate.ToString("dd/MM/yyyy")
        };

        DwfStampManager.ReplaceStampPlaceholders(outputPath, replacements);
    }

    private static void StampPdfFile(string sourcePath, StampContentInfo stampContent, string outputPath, int[]? selectedIndices)
    {
        if (selectedIndices is { Length: > 0 })
        {
            bool first = true;
            foreach (var pageIndex in selectedIndices)
            {
                var source = first ? sourcePath : outputPath;
                PdfStampManager.GenerateAndApplyStamp(source, outputPath,
                    new PdfGeneratedStampOptions
                    {
                        Title = stampContent.ApprovalText,
                        InspectorName = stampContent.InspectorName,
                        StampDate = stampContent.StampDate,
                        Placement = StampPlacement.BottomRight,
                        PageIndex = pageIndex,
                        StampAllPages = false,
                        OverwriteOutput = true
                    });
                first = false;
            }
        }
        else
        {
            PdfStampManager.GenerateAndApplyStamp(sourcePath, outputPath,
                new PdfGeneratedStampOptions
                {
                    Title = stampContent.ApprovalText,
                    InspectorName = stampContent.InspectorName,
                    StampDate = stampContent.StampDate,
                    Placement = StampPlacement.BottomRight,
                    StampAllPages = true,
                    OverwriteOutput = true
                });
        }
    }

    private static int[]? ParseLayoutIndices(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<int[]>(json);
        }
        catch
        {
            return null;
        }
    }
}
