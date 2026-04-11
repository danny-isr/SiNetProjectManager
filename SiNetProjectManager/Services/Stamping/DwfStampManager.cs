using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SiNetProjectManager.Services.Stamping;

public sealed class DwfLayoutInfo
{
    public int Index { get; init; }
    public string SectionName { get; init; } = "";
    public string LayoutName { get; init; } = "";
    public string DescriptorPath { get; init; } = "";
    public bool HasStamp { get; init; }

    public override string ToString()
        => $"{Index}: {LayoutName} (Section={SectionName}, HasStamp={HasStamp})";
}

public sealed class DwfStampInsertOptions
{
    public string? LayoutName { get; set; }
    public int? LayoutIndex { get; set; }

    public bool ThrowIfStampAlreadyExists { get; set; }
    public bool OverwriteOutput { get; set; } = true;
}

public sealed class DwfStampInsertResult
{
    public bool Success { get; init; }
    public bool StampAlreadyExisted { get; init; }
    public string OutputPath { get; init; } = "";
    public string TargetLayoutName { get; init; } = "";
    public int TargetLayoutIndex { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Static utility for inspecting and inserting stamp (markup) resources
/// into Autodesk DWF files (ZIP-based ePlot archives).
/// </summary>
public static class DwfStampManager
{
    private static readonly XNamespace DwfNs = "DWF-Manifest:6.0";
    private static readonly XNamespace EPlotNs = "DWF-ePlot:1.2";
    private static readonly XNamespace EPlotGlobalNs = "DWF-ePlot:0.0";

    private const string GlobalTypeInfoPath = "715941D4-1AC2-4545-8185-BC40E053B551.TYPEINFO";

    private static readonly string[] LayoutStampRoles =
    [
        "2d vector markup",
        "markup object definition",
        "markup private"
    ];

    // ── DWF magic header ───────────────────────────────────────────────
    //
    // DWF files are ZIP archives prefixed with a 12-byte magic header
    // (e.g., "(DWF V06.20)") placed before the standard ZIP "PK" signature.
    //
    // .NET's ZipArchive can READ such files (it finds the EOCD from the
    // end of the stream and resolves offsets), but WRITE operations in
    // Update mode may overwrite bytes at stream position 0 when entries
    // are deleted + re-created (e.g. SaveXml), destroying any DWF magic
    // header stored there.
    //
    // All write paths therefore follow this THREE-PHASE protocol:
    //   Phase 1 – Create a PURE ZIP MemoryStream (no magic header) from
    //             the source DWF. Entries start at stream position 0.
    //   Phase 2 – Open that pure ZIP in Update mode and make all
    //             modifications safely (no header at position 0 to lose).
    //   Phase 3 – Repack into a FINAL stream: write the 12-byte magic
    //             header first, then create a fresh ZipArchive in Create
    //             mode at position 12 and copy all modified entries.
    //             Central Directory offsets will correctly account for the
    //             12-byte prefix.
    //
    // For read-only paths (e.g. ReplaceStampPlaceholders), the simpler
    // CreateCleanZipStream helper (header-first) is still valid because
    // no Update-mode re-creation occurs.
    //

    private const int DwfMagicHeaderSize = 12;

    // ── Query

    /// <summary>
    /// Returns a diagnostic dump of the DWF archive structure:
    /// ZIP entries, sizes, and manifest section info.
    /// </summary>
    public static string DiagnoseDwf(string dwfPath)
    {
        var sb = new StringBuilder();

        try
        {
            var fi = new FileInfo(dwfPath);
            sb.AppendLine($"File: {fi.Name} | Size: {fi.Length:N0} bytes");

            using var archive = ZipFile.OpenRead(dwfPath);
            sb.AppendLine($"ZIP entries: {archive.Entries.Count}");

            foreach (var entry in archive.Entries.OrderBy(e => e.FullName))
            {
                sb.AppendLine($"  {entry.FullName} | raw: {entry.Length:N0} | compressed: {entry.CompressedLength:N0}");
            }

            var manifestEntry = archive.GetEntry("manifest.xml");
            if (manifestEntry != null)
            {
                using var stream = manifestEntry.Open();
                var doc = XDocument.Load(stream);
                var sections = doc.Root?.Element(DwfNs + "Sections");

                if (sections != null)
                {
                    foreach (var section in sections.Elements(DwfNs + "Section"))
                    {
                        var type = (string?)section.Attribute("type") ?? "?";
                        var name = (string?)section.Attribute("name") ?? "?";
                        var toc = section.Element(DwfNs + "Toc");
                        var resources = toc?.Elements(DwfNs + "Resource").ToList() ?? [];

                        sb.AppendLine($"  Section: {name} | type: {type} | resources: {resources.Count}");
                        foreach (var r in resources)
                        {
                            sb.AppendLine($"    role: {(string?)r.Attribute("role")} | href: {(string?)r.Attribute("href")}");
                        }
                    }
                }

                sb.AppendLine("manifest.xml: OK");
            }
            else
            {
                sb.AppendLine("manifest.xml: MISSING!");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
        }

        return sb.ToString();
    }

    public static IReadOnlyList<DwfLayoutInfo> GetLayouts(string dwfZipPath)
    {
        if (!File.Exists(dwfZipPath))
            throw new FileNotFoundException("DWF zip file not found.", dwfZipPath);

        using var archive = ZipFile.OpenRead(dwfZipPath);
        var manifest = LoadXml(archive, "manifest.xml");

        var result = new List<DwfLayoutInfo>();
        int index = 0;

        foreach (var section in GetEplotSections(manifest))
        {
            var toc = GetRequiredElement(section, DwfNs + "Toc");
            var descriptorPath = GetDescriptorHref(toc);
            var descriptor = LoadXml(archive, descriptorPath);
            var layoutName = ReadLayoutName(descriptor);

            result.Add(new DwfLayoutInfo
            {
                Index = index,
                SectionName = (string?)section.Attribute("name") ?? "",
                LayoutName = layoutName,
                DescriptorPath = descriptorPath,
                HasStamp = SectionHasStamp(section)
            });

            index++;
        }

        return result;
    }

    public static bool HasStamp(string dwfZipPath, string layoutName)
    {
        var layout = GetLayouts(dwfZipPath)
            .FirstOrDefault(x => string.Equals(x.LayoutName, layoutName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Layout '{layoutName}' not found.");

        return layout.HasStamp;
    }

    public static bool HasStamp(string dwfZipPath, int layoutIndex)
    {
        var layout = GetLayouts(dwfZipPath)
            .FirstOrDefault(x => x.Index == layoutIndex)
            ?? throw new InvalidOperationException($"Layout index '{layoutIndex}' not found.");

        return layout.HasStamp;
    }

    // ── Stamp insertion ────────────────────────────────────────────────

    public static DwfStampInsertResult AddStampFromTemplate(
        string sourceDwfZipPath,
        string templateStampedDwfZipPath,
        string outputDwfZipPath,
        DwfStampInsertOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputFiles(sourceDwfZipPath, templateStampedDwfZipPath);

        if (File.Exists(outputDwfZipPath))
        {
            if (!options.OverwriteOutput)
                throw new IOException($"Output file already exists: {outputDwfZipPath}");

            File.Delete(outputDwfZipPath);
        }

        // ──────────────────────────────────────────────────────────────────
        // THREE-PHASE APPROACH: pure ZIP → modify → repack with magic header
        //
        // .NET's ZipArchive in Update mode may overwrite bytes at position 0
        // of the underlying stream (e.g. when SaveXml deletes + re-creates
        // an entry). This destroys any DWF magic header stored there.
        //
        // Solution:
        //   Phase 1 – Create a pure ZIP (no magic header) from the source.
        //   Phase 2 – Modify it safely with ZipArchive in Update mode.
        //   Phase 3 – Repack into a new stream: [magic header][ZIP data]
        //             so Central Directory offsets account for the 12-byte prefix.
        // ──────────────────────────────────────────────────────────────────

        // Phase 1: Create a pure ZIP MemoryStream (no magic header) from source.
        // This is safe for Update mode — nothing at position 0 to corrupt.
        using var pureZipStream = new MemoryStream();
        using (var source = ZipFile.OpenRead(sourceDwfZipPath))
        using (var fresh = new ZipArchive(pureZipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var newEntry = fresh.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                newEntry.LastWriteTime = entry.LastWriteTime;

                using var src = entry.Open();
                using var dst = newEntry.Open();
                src.CopyTo(dst);
            }
        }

        // Template reading — ZipFile.OpenRead handles the magic header offset
        using var templateArchive = ZipFile.OpenRead(templateStampedDwfZipPath);

        DwfStampInsertResult result;

        // Phase 2: Modify the pure ZIP in Update mode (no magic header to lose).
        pureZipStream.Position = 0;
        using (var sourceArchive = new ZipArchive(pureZipStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var sourceManifest = LoadXml(sourceArchive, "manifest.xml");
            var templateManifest = LoadXml(templateArchive, "manifest.xml");

            var sourceLayouts = GetLayoutSections(sourceArchive, sourceManifest).ToList();
            var target = ResolveTargetLayout(sourceLayouts, options);

            if (target.HasStamp)
            {
                if (options.ThrowIfStampAlreadyExists)
                {
                    throw new InvalidOperationException(
                        $"Layout '{target.LayoutName}' already contains a stamp.");
                }

                // No modifications needed — copy source as-is (preserves header)
                File.Copy(sourceDwfZipPath, outputDwfZipPath, true);

                return new DwfStampInsertResult
                {
                    Success = true,
                    StampAlreadyExisted = true,
                    OutputPath = outputDwfZipPath,
                    TargetLayoutName = target.LayoutName,
                    TargetLayoutIndex = target.Index,
                    Message = "Stamp already exists. No changes were made."
                };
            }

            var templateStampedSection = FindFirstStampedLayoutSection(templateArchive, templateManifest);

            if (templateStampedSection == null)
            {
                var templateLayouts = GetLayoutSections(templateArchive, templateManifest).ToList();
                var templateLayoutInfo = string.Join("\n  ", templateLayouts.Select(
                    l => $"[{l.Index}] {l.LayoutName} (HasStamp={l.HasStamp})"));

                throw new InvalidOperationException(
                    $"Template DWF does not contain a stamped layout.\n" +
                    $"Template layouts found:\n  {templateLayoutInfo}");
            }

            var templateStamped = templateStampedSection.Value;

            EnsureGlobalStampInfrastructure(
                sourceArchive,
                templateArchive,
                sourceManifest,
                templateManifest);

            CopyStampResourcesToTargetLayout(
                sourceArchive,
                templateArchive,
                target.Section,
                templateStamped.Section);

            AdjustStampPositionForPageSize(
                sourceArchive,
                templateArchive,
                target.Section,
                templateStamped.Section);

            SaveXml(sourceArchive, "manifest.xml", sourceManifest);

            result = new DwfStampInsertResult
            {
                Success = true,
                StampAlreadyExisted = false,
                OutputPath = outputDwfZipPath,
                TargetLayoutName = target.LayoutName,
                TargetLayoutIndex = target.Index,
                Message = $"Stamp inserted successfully into layout '{target.LayoutName}' (index {target.Index}) from template layout '{templateStamped.LayoutName}' (index {templateStamped.Index})."
            };
        }

        // Phase 3: Repack modified ZIP with the source's magic header.
        // Writing header first ensures Central Directory offsets in the
        // final ZIP account for the 12-byte DWF prefix.
        var magicHeader = ReadMagicHeader(sourceDwfZipPath);
        using var finalStream = new MemoryStream();
        finalStream.Write(magicHeader, 0, magicHeader.Length);

        pureZipStream.Position = 0;
        using (var modified = new ZipArchive(pureZipStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var final = new ZipArchive(finalStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in modified.Entries)
            {
                var newEntry = final.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                newEntry.LastWriteTime = entry.LastWriteTime;

                using var src = entry.Open();
                using var dst = newEntry.Open();
                src.CopyTo(dst);
            }
        }

        finalStream.Position = 0;
        using (var fs = File.Create(outputDwfZipPath))
        {
            finalStream.CopyTo(fs);
            fs.Flush();
        }

        return result;
    }

    // ── Private helpers ────────────────────────────────────────────────

    private static void ValidateInputFiles(string sourceDwfZipPath, string templateStampedDwfZipPath)
    {
        if (!File.Exists(sourceDwfZipPath))
            throw new FileNotFoundException("Source DWF zip file not found.", sourceDwfZipPath);

        if (!File.Exists(templateStampedDwfZipPath))
            throw new FileNotFoundException("Stamped template DWF zip file not found.", templateStampedDwfZipPath);
    }

    private static IEnumerable<XElement> GetEplotSections(XDocument manifest)
    {
        return GetRequiredElement(manifest.Root!, DwfNs + "Sections")
            .Elements(DwfNs + "Section")
            .Where(s => string.Equals(
                (string?)s.Attribute("type"),
                "com.autodesk.dwf.ePlot",
                StringComparison.Ordinal));
    }

    private static IEnumerable<(int Index, XElement Section, string LayoutName, bool HasStamp)>
        GetLayoutSections(ZipArchive archive, XDocument manifest)
    {
        int index = 0;

        foreach (var section in GetEplotSections(manifest))
        {
            var toc = GetRequiredElement(section, DwfNs + "Toc");
            var descriptorPath = GetDescriptorHref(toc);
            var descriptor = LoadXml(archive, descriptorPath);

            yield return (index, section, ReadLayoutName(descriptor), SectionHasStamp(section));
            index++;
        }
    }

    private static (int Index, XElement Section, string LayoutName, bool HasStamp) ResolveTargetLayout(
        List<(int Index, XElement Section, string LayoutName, bool HasStamp)> layouts,
        DwfStampInsertOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.LayoutName))
        {
            var match = layouts.FirstOrDefault(x =>
                string.Equals(x.LayoutName, options.LayoutName, StringComparison.OrdinalIgnoreCase));

            if (match.Section == null)
                throw new InvalidOperationException($"Layout '{options.LayoutName}' not found.");

            return match;
        }

        if (options.LayoutIndex.HasValue)
        {
            var match = layouts.FirstOrDefault(x => x.Index == options.LayoutIndex.Value);

            if (match.Section == null)
                throw new InvalidOperationException($"Layout index '{options.LayoutIndex.Value}' not found.");

            return match;
        }

        throw new InvalidOperationException("You must provide either LayoutName or LayoutIndex.");
    }

    private static (int Index, XElement Section, string LayoutName, bool HasStamp)?
        FindFirstStampedLayoutSection(ZipArchive archive, XDocument manifest)
    {
        foreach (var item in GetLayoutSections(archive, manifest))
        {
            if (item.HasStamp)
                return item;
        }

        return null;
    }

    private static bool SectionHasStamp(XElement section)
    {
        var toc = GetRequiredElement(section, DwfNs + "Toc");

        return toc.Elements(DwfNs + "Resource")
            .Any(r =>
            {
                var role = (string?)r.Attribute("role");
                return LayoutStampRoles.Contains(role ?? "", StringComparer.OrdinalIgnoreCase);
            });
    }

    private static void EnsureGlobalStampInfrastructure(
        ZipArchive sourceArchive,
        ZipArchive templateArchive,
        XDocument sourceManifest,
        XDocument templateManifest)
    {
        EnsureTypeInfo(sourceArchive, templateArchive);
        EnsureContentXml(sourceArchive, templateArchive, sourceManifest, templateManifest);
        EnsureGlobalMarkupDefinition(sourceArchive, templateArchive, sourceManifest, templateManifest);
    }

    private static void EnsureTypeInfo(ZipArchive sourceArchive, ZipArchive templateArchive)
    {
        if (sourceArchive.GetEntry(GlobalTypeInfoPath) == null)
        {
            CopyEntry(templateArchive, sourceArchive, GlobalTypeInfoPath, GlobalTypeInfoPath);
        }
    }

    private static void EnsureContentXml(
        ZipArchive sourceArchive,
        ZipArchive templateArchive,
        XDocument sourceManifest,
        XDocument templateManifest)
    {
        var sourceRoot = sourceManifest.Root ?? throw new InvalidOperationException("Invalid source manifest.");
        var templateRoot = templateManifest.Root ?? throw new InvalidOperationException("Invalid template manifest.");

        var sourceContents = sourceRoot.Element(DwfNs + "Contents");
        if (sourceContents != null)
            return;

        var templateContents = templateRoot.Element(DwfNs + "Contents");
        if (templateContents == null)
            return;

        sourceRoot.AddFirst(new XElement(templateContents));

        var contentHref = templateContents
            .Element(DwfNs + "Content")
            ?.Attribute("href")?.Value;

        if (!string.IsNullOrWhiteSpace(contentHref) && sourceArchive.GetEntry(contentHref) == null)
        {
            CopyEntry(templateArchive, sourceArchive, contentHref, contentHref);
        }
    }

    private static void EnsureGlobalMarkupDefinition(
        ZipArchive sourceArchive,
        ZipArchive templateArchive,
        XDocument sourceManifest,
        XDocument templateManifest)
    {
        var sourceGlobalSection = GetGlobalSection(sourceManifest);
        var templateGlobalSection = GetGlobalSection(templateManifest);

        var sourceGlobalToc = GetRequiredElement(sourceGlobalSection, DwfNs + "Toc");
        var templateGlobalToc = GetRequiredElement(templateGlobalSection, DwfNs + "Toc");

        bool sourceHasGlobalMarkupResource = sourceGlobalToc
            .Elements(DwfNs + "Resource")
            .Any(r => string.Equals(
                (string?)r.Attribute("role"),
                "markup object definition",
                StringComparison.OrdinalIgnoreCase));

        if (!sourceHasGlobalMarkupResource)
        {
            var templateMarkupResource = templateGlobalToc
                .Elements(DwfNs + "Resource")
                .FirstOrDefault(r => string.Equals(
                    (string?)r.Attribute("role"),
                    "markup object definition",
                    StringComparison.OrdinalIgnoreCase));

            if (templateMarkupResource != null)
            {
                sourceGlobalToc.Add(new XElement(templateMarkupResource));

                var href = (string?)templateMarkupResource.Attribute("href");
                if (!string.IsNullOrWhiteSpace(href) && sourceArchive.GetEntry(href) == null)
                {
                    CopyEntry(templateArchive, sourceArchive, href, href);
                }
            }
        }

        var sourceGlobalDescriptorPath = GetDescriptorHref(sourceGlobalToc);
        var templateGlobalDescriptorPath = GetDescriptorHref(templateGlobalToc);

        var sourceGlobalDescriptor = LoadXml(sourceArchive, sourceGlobalDescriptorPath);
        var templateGlobalDescriptor = LoadXml(templateArchive, templateGlobalDescriptorPath);

        var sourceResources = sourceGlobalDescriptor.Root!.Element(EPlotGlobalNs + "Resources");
        if (sourceResources == null)
        {
            sourceResources = new XElement(EPlotGlobalNs + "Resources");
            sourceGlobalDescriptor.Root!.Add(sourceResources);
        }

        var templateResources = templateGlobalDescriptor.Root!.Element(EPlotGlobalNs + "Resources");
        if (templateResources == null)
            return; // Template descriptor has no Resources section — nothing to copy

        bool sourceDescriptorHasMarkup = sourceResources
            .Elements(EPlotGlobalNs + "Resource")
            .Any(r => string.Equals(
                (string?)r.Attribute("role"),
                "markup object definition",
                StringComparison.OrdinalIgnoreCase));

        if (!sourceDescriptorHasMarkup)
        {
            var templateMarkup = templateResources
                .Elements(EPlotGlobalNs + "Resource")
                .FirstOrDefault(r => string.Equals(
                    (string?)r.Attribute("role"),
                    "markup object definition",
                    StringComparison.OrdinalIgnoreCase));

            if (templateMarkup != null)
            {
                sourceResources.Add(new XElement(templateMarkup));
                SaveXml(sourceArchive, sourceGlobalDescriptorPath, sourceGlobalDescriptor);
            }
        }
    }

    private static XElement GetGlobalSection(XDocument manifest)
    {
        return GetRequiredElement(manifest.Root!, DwfNs + "Sections")
            .Elements(DwfNs + "Section")
            .First(s => string.Equals(
                (string?)s.Attribute("type"),
                "com.autodesk.dwf.ePlotGlobal",
                StringComparison.Ordinal));
    }

    private static void CopyStampResourcesToTargetLayout(
        ZipArchive sourceArchive,
        ZipArchive templateArchive,
        XElement targetSection,
        XElement templateStampedSection)
    {
        var targetToc = GetRequiredElement(targetSection, DwfNs + "Toc");
        var templateToc = GetRequiredElement(templateStampedSection, DwfNs + "Toc");

        var targetSectionName = (string?)targetSection.Attribute("name")
            ?? throw new InvalidOperationException("Target section has no name.");
        var templateSectionName = (string?)templateStampedSection.Attribute("name")
            ?? throw new InvalidOperationException("Template section has no name.");

        // IMPORTANT: If both sections have the same name, we still need to copy the files
        // but the href paths will be identical (no replacement needed).
        bool sameSection = string.Equals(targetSectionName, templateSectionName, StringComparison.Ordinal);

        foreach (var role in LayoutStampRoles)
        {
            bool alreadyExists = targetToc
                .Elements(DwfNs + "Resource")
                .Any(r => string.Equals(
                    (string?)r.Attribute("role"), role, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
                continue;

            var templateResource = templateToc
                .Elements(DwfNs + "Resource")
                .FirstOrDefault(r => string.Equals(
                    (string?)r.Attribute("role"), role, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Template is missing layout resource '{role}'.");

            var templateHref = (string?)templateResource.Attribute("href");
            if (string.IsNullOrWhiteSpace(templateHref))
                throw new InvalidOperationException($"Template resource '{role}' has no href.");

            // Replace template section name with target section name in href
            // Example: "Section-01/2d vector markup.w2d" → "Section-02/2d vector markup.w2d"
            // If both sections have the same name, targetHref == templateHref
            var targetHref = sameSection
                ? templateHref
                : templateHref.Replace(templateSectionName, targetSectionName);

            // Create new resource element with updated href
            var targetResource = new XElement(templateResource);
            targetResource.SetAttributeValue("href", targetHref);
            targetToc.Add(targetResource);

            // Copy the file content from template to target path
            // Skip if the file already exists (e.g., when source == template)
            if (sourceArchive.GetEntry(targetHref) == null)
            {
                CopyEntry(templateArchive, sourceArchive, templateHref, targetHref);
            }
        }

        var targetDescriptorPath = GetDescriptorHref(targetToc);
        var templateDescriptorPath = GetDescriptorHref(templateToc);

        var targetDescriptor = LoadXml(sourceArchive, targetDescriptorPath);
        var templateDescriptor = LoadXml(templateArchive, templateDescriptorPath);

        var targetResources = targetDescriptor.Root!.Element(EPlotNs + "Resources");
        if (targetResources == null)
        {
            targetResources = new XElement(EPlotNs + "Resources");
            targetDescriptor.Root!.Add(targetResources);
        }

        var templateResources = templateDescriptor.Root!.Element(EPlotNs + "Resources");
        if (templateResources == null)
        {
            SaveXml(sourceArchive, targetDescriptorPath, targetDescriptor);
            return;
        }

        foreach (var role in LayoutStampRoles)
        {
            // Search ALL child element types (Resource, GraphicResource,
            // ImageResource) — "2d vector markup" is a GraphicResource.
            bool existsInDescriptor = targetResources
                .Elements()
                .Any(r => string.Equals(
                    (string?)r.Attribute("role"), role, StringComparison.OrdinalIgnoreCase));

            if (existsInDescriptor)
                continue;

            var templateNode = templateResources
                .Elements()
                .FirstOrDefault(r => string.Equals(
                    (string?)r.Attribute("role"), role, StringComparison.OrdinalIgnoreCase));

            if (templateNode != null)
            {
                var targetNode = new XElement(templateNode);

                // Update href to point to target section path
                // If both sections have the same name, no replacement needed
                var templateHref = (string?)templateNode.Attribute("href");
                if (!string.IsNullOrWhiteSpace(templateHref) && !sameSection)
                {
                    var targetHref = templateHref.Replace(templateSectionName, targetSectionName);
                    targetNode.SetAttributeValue("href", targetHref);
                }

                targetResources.Add(targetNode);
            }
        }

        SaveXml(sourceArchive, targetDescriptorPath, targetDescriptor);
    }

    /// <summary>
    /// Adjusts the stamp X position in the markup private XML so it maintains
    /// the same distance from the right edge of the target page as it had in
    /// the template page. This ensures the stamp always appears flush-right
    /// regardless of differences in paper size between template and target.
    /// </summary>
    private static void AdjustStampPositionForPageSize(
        ZipArchive sourceArchive,
        ZipArchive templateArchive,
        XElement targetSection,
        XElement templateStampedSection)
    {
        var targetToc = GetRequiredElement(targetSection, DwfNs + "Toc");
        var templateToc = GetRequiredElement(templateStampedSection, DwfNs + "Toc");

        var targetDesc = LoadXml(sourceArchive, GetDescriptorHref(targetToc));
        var templateDesc = LoadXml(templateArchive, GetDescriptorHref(templateToc));

        double targetWidthIn = PaperWidthInInches(targetDesc.Root!.Element(EPlotNs + "Paper"));
        double templateWidthIn = PaperWidthInInches(templateDesc.Root!.Element(EPlotNs + "Paper"));

        if (targetWidthIn <= 0 || templateWidthIn <= 0)
            return;

        double shiftInches = targetWidthIn - templateWidthIn;
        if (Math.Abs(shiftInches) < 0.001)
            return; // Same paper width — no adjustment needed.

        // Find the markup private entry in the target archive.
        var markupPrivateHref = targetToc
            .Elements(DwfNs + "Resource")
            .FirstOrDefault(r => string.Equals(
                (string?)r.Attribute("role"), "markup private",
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("href")?.Value;

        if (string.IsNullOrEmpty(markupPrivateHref)
            || sourceArchive.GetEntry(markupPrivateHref) == null)
            return;

        var markupDoc = LoadXml(sourceArchive, markupPrivateHref);

        bool modified = false;
        foreach (var text2d in markupDoc.Descendants("Text2D"))
        {
            var xAttr = text2d.Attribute("X");
            if (xAttr == null) continue;

            if (!double.TryParse(xAttr.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double originalX))
                continue;

            double newX = originalX + shiftInches;
            xAttr.Value = newX.ToString(CultureInfo.InvariantCulture);
            modified = true;
        }

        if (modified)
            SaveXml(sourceArchive, markupPrivateHref, markupDoc);
    }

    /// <summary>
    /// Reads the paper width from an <c>ePlot:Paper</c> element and returns
    /// the value converted to inches. Handles units="mm", "cm", and "in".
    /// </summary>
    private static double PaperWidthInInches(XElement? paper)
    {
        if (paper == null) return 0;

        double width = (double?)paper.Attribute("width") ?? 0;
        if (width <= 0) return 0;

        string units = (string?)paper.Attribute("units") ?? "mm";
        return units switch
        {
            "in" => width,
            "cm" => width / 2.54,
            _    => width / 25.4  // mm (default)
        };
    }

    private static string ReadLayoutName(XDocument descriptor)
    {
        var properties = descriptor.Root?.Element(EPlotNs + "Properties");
        if (properties == null)
            return "";

        var layoutProp = properties.Elements(EPlotNs + "Property")
            .FirstOrDefault(p => string.Equals(
                (string?)p.Attribute("name"),
                "Layout Name",
                StringComparison.OrdinalIgnoreCase));

        return (string?)layoutProp?.Attribute("value") ?? "";
    }

    private static string GetDescriptorHref(XElement toc)
    {
        var href = toc
            .Elements(DwfNs + "Resource")
            .FirstOrDefault(r => string.Equals(
                (string?)r.Attribute("role"),
                "descriptor",
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("href")?.Value;

        if (string.IsNullOrWhiteSpace(href))
            throw new InvalidOperationException("Descriptor resource not found.");

        return href;
    }

    private static XDocument LoadXml(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath)
            ?? throw new InvalidOperationException($"ZIP entry '{entryPath}' was not found.");

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void SaveXml(ZipArchive archive, string entryPath, XDocument doc)
    {
        var old = archive.GetEntry(entryPath);
        old?.Delete();

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        doc.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void CopyEntry(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceEntryPath,
        string targetEntryPath)
    {
        var sourceEntry = sourceArchive.GetEntry(sourceEntryPath)
            ?? throw new InvalidOperationException($"Template entry '{sourceEntryPath}' was not found.");

        var existing = targetArchive.GetEntry(targetEntryPath);
        existing?.Delete();

        var targetEntry = targetArchive.CreateEntry(targetEntryPath, CompressionLevel.Optimal);

        using var input = sourceEntry.Open();
        using var output = targetEntry.Open();
        input.CopyTo(output);
    }

    /// <summary>
    /// Reads the 12-byte DWF magic header (e.g. "(DWF V06.20)") from the
    /// beginning of the file.
    /// </summary>
    private static byte[] ReadMagicHeader(string dwfPath)
    {
        using var fs = File.OpenRead(dwfPath);
        var header = new byte[DwfMagicHeaderSize];
        fs.ReadExactly(header, 0, DwfMagicHeaderSize);
        return header;
    }

    /// <summary>
    /// Creates a complete DWF <see cref="MemoryStream"/> (magic header + ZIP)
    /// from a source DWF file by reading all entries via <see cref="ZipFile.OpenRead"/>
    /// and repacking them into a fresh ZIP with correct internal offsets.
    /// <para>
    /// The magic header is written FIRST, then the ZIP content is created
    /// starting at position 12. This ensures the Central Directory offsets
    /// account for the 12-byte prefix, producing a valid DWF structure.
    /// </para>
    /// </summary>
    /// <returns>A MemoryStream containing: [12-byte magic header][ZIP content]</returns>
    private static MemoryStream CreateCleanZipStream(string dwfPath, byte[] magicHeader)
    {
        var ms = new MemoryStream();

        // Write magic header first (positions 0-11)
        ms.Write(magicHeader, 0, magicHeader.Length);

        // Now create the ZIP starting at position 12.
        // ZipArchive will calculate all Central Directory offsets from the
        // stream's current position, so they will correctly point to >= 12.
        using (var source = ZipFile.OpenRead(dwfPath))
        using (var fresh = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            int entryCount = 0;
            foreach (var entry in source.Entries)
            {
                var newEntry = fresh.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                newEntry.LastWriteTime = entry.LastWriteTime;

                using var src = entry.Open();
                using var dst = newEntry.Open();
                src.CopyTo(dst);

                entryCount++;
            }

            if (entryCount == 0)
                throw new InvalidOperationException($"Source DWF '{dwfPath}' contains no ZIP entries.");
        }

        ms.Position = 0;
        return ms;
    }

    private static XElement GetRequiredElement(XElement parent, XName name)
    {
        return parent.Element(name)
               ?? throw new InvalidOperationException($"Required element '{name}' not found.");
    }

    // ── Placeholder replacement ────────────────────────────────────────

    /// <summary>
    /// Well-known placeholder strings for DWF stamp templates.
    /// Each placeholder MUST be replaced with a string of the exact same character count.
    /// </summary>
    public static class StampPlaceholders
    {
        /// <summary>Date placeholder (10 chars). Replace with dd/MM/yyyy formatted date.</summary>
        public const string Date = "DD/MM/YYYY";
    }

    /// <summary>
    /// Replaces placeholder text within the stamp markup resources of a DWF file.
    /// Each placeholder must map to a replacement string of the <b>exact same character count</b>
    /// to keep byte offsets valid in both XML and W2D binary entries.
    /// <para>
    /// The method searches XML files (markup private, markup object definition) and
    /// W2D files using both UTF-8 and UTF-16LE byte patterns.
    /// </para>
    /// </summary>
    /// <returns>Total number of byte-pattern replacements performed.</returns>
    public static int ReplaceStampPlaceholders(
        string dwfPath,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (!File.Exists(dwfPath))
            throw new FileNotFoundException("DWF file not found.", dwfPath);

        if (replacements.Count == 0)
            return 0;

        foreach (var (key, value) in replacements)
        {
            if (key.Length != value.Length)
                throw new ArgumentException(
                    $"Placeholder '{key}' ({key.Length} chars) and replacement " +
                    $"'{value}' ({value.Length} chars) must have identical character counts.");
        }

        var magicHeader = ReadMagicHeader(dwfPath);
        int totalReplacements = 0;

        // Single-pass: read source → replace in markup entries → write with magic header.
        using var finalStream = new MemoryStream();
        finalStream.Write(magicHeader, 0, magicHeader.Length);

        using (var source = ZipFile.OpenRead(dwfPath))
        using (var target = new ZipArchive(finalStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                bool isMarkupEntry =
                    entry.FullName.EndsWith(".w2d", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

                var newEntry = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                newEntry.LastWriteTime = entry.LastWriteTime;

                if (isMarkupEntry)
                {
                    using var srcStream = entry.Open();
                    using var ms = new MemoryStream();
                    srcStream.CopyTo(ms);
                    var data = ms.ToArray();

                    foreach (var (placeholder, replacement) in replacements)
                    {
                        // UTF-8 (covers XML files where text is stored as attribute values)
                        totalReplacements += ReplaceBytesInPlace(
                            data,
                            Encoding.UTF8.GetBytes(placeholder),
                            Encoding.UTF8.GetBytes(replacement));

                        // UTF-16LE (covers W2D binary text opcodes)
                        totalReplacements += ReplaceBytesInPlace(
                            data,
                            Encoding.Unicode.GetBytes(placeholder),
                            Encoding.Unicode.GetBytes(replacement));
                    }

                    using var dstStream = newEntry.Open();
                    dstStream.Write(data, 0, data.Length);
                }
                else
                {
                    using var src = entry.Open();
                    using var dst = newEntry.Open();
                    src.CopyTo(dst);
                }
            }
        }

        // Write back to the same file (header + ZIP with correct offsets).
        finalStream.Position = 0;
        using (var fs = File.Create(dwfPath))
        {
            finalStream.CopyTo(fs);
            fs.Flush();
        }

        return totalReplacements;
    }

    /// <summary>
    /// Scans <paramref name="data"/> for every occurrence of <paramref name="pattern"/>
    /// and overwrites each one with <paramref name="replacement"/> (same byte length).
    /// </summary>
    private static int ReplaceBytesInPlace(byte[] data, byte[] pattern, byte[] replacement)
    {
        if (pattern.Length != replacement.Length || pattern.Length == 0)
            return 0;

        int count = 0;
        int limit = data.Length - pattern.Length;

        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                Buffer.BlockCopy(replacement, 0, data, i, replacement.Length);
                i += replacement.Length - 1; // skip past replacement
                count++;
            }
        }

        return count;
    }

    // ── Self-test / verification ───────────────────────────────────────
    //
    // C# equivalent of Python's `if __name__ == "__main__":` block.
    // Call DwfStampManager.VerifyMagicHeader("path/to/file.dwf") from
    // a test runner, debug console, or Quick Action to validate DWF integrity.
    //

    /// <summary>
    /// Reads the first 12 bytes of a DWF file and verifies the magic header
    /// matches the expected <c>(DWF Vxx.xx)</c> format. Returns a diagnostic
    /// string describing the header and ZIP signature status.
    /// </summary>
    public static string VerifyMagicHeader(string dwfPath)
    {
        if (!File.Exists(dwfPath))
            throw new FileNotFoundException("DWF file not found.", dwfPath);

        var fileBytes = File.ReadAllBytes(dwfPath);

        if (fileBytes.Length < DwfMagicHeaderSize + 4)
            return $"INVALID: File too small ({fileBytes.Length} bytes).";

        // Extract the 12-byte magic header as ASCII text
        var headerText = Encoding.ASCII.GetString(fileBytes, 0, DwfMagicHeaderSize);

        // Verify the ZIP "PK" signature starts immediately after the header
        bool hasZipSignature = fileBytes[DwfMagicHeaderSize] == 0x50  // 'P'
                            && fileBytes[DwfMagicHeaderSize + 1] == 0x4B; // 'K'

        var status = hasZipSignature ? "OK" : "MISSING ZIP SIGNATURE";

        return $"Header: \"{headerText}\" | ZIP @ offset {DwfMagicHeaderSize}: {status} | File size: {fileBytes.Length:N0} bytes";
    }

    /// <summary>
    /// Compares two DWF files (original vs. stamped) and generates a detailed
    /// text report showing all structural differences, added resources, and
    /// manifest/descriptor changes. Saves the report to a text file.
    /// </summary>
    /// <param name="originalDwfPath">Path to original DWF (without stamp)</param>
    /// <param name="stampedDwfPath">Path to stamped DWF (with stamp)</param>
    /// <param name="outputReportPath">Path where the analysis report will be saved</param>
    public static void CompareAndGenerateReport(
        string originalDwfPath,
        string stampedDwfPath,
        string outputReportPath)
    {
        if (!File.Exists(originalDwfPath))
            throw new FileNotFoundException("Original DWF not found.", originalDwfPath);

        if (!File.Exists(stampedDwfPath))
            throw new FileNotFoundException("Stamped DWF not found.", stampedDwfPath);

        var sb = new StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("  DWF STAMP ANALYSIS REPORT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Original DWF: {Path.GetFileName(originalDwfPath)}");
        sb.AppendLine($"Stamped DWF:  {Path.GetFileName(stampedDwfPath)}");
        sb.AppendLine();

        // ── 1. Magic Header Verification ──────────────────────────────────
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("1. MAGIC HEADER");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"Original: {VerifyMagicHeader(originalDwfPath)}");
        sb.AppendLine($"Stamped:  {VerifyMagicHeader(stampedDwfPath)}");
        sb.AppendLine();

        // ── 2. Layout Comparison ───────────────────────────────────────────
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("2. LAYOUTS");
        sb.AppendLine("───────────────────────────────────────────────────────────────");

        var originalLayouts = GetLayouts(originalDwfPath);
        var stampedLayouts = GetLayouts(stampedDwfPath);

        sb.AppendLine($"Original layouts: {originalLayouts.Count}");
        foreach (var layout in originalLayouts)
        {
            sb.AppendLine($"  [{layout.Index}] {layout.LayoutName}");
            sb.AppendLine($"      Section:    {layout.SectionName}");
            sb.AppendLine($"      Descriptor: {layout.DescriptorPath}");
            sb.AppendLine($"      HasStamp:   {layout.HasStamp}");
            sb.AppendLine();
        }

        sb.AppendLine($"Stamped layouts: {stampedLayouts.Count}");
        foreach (var layout in stampedLayouts)
        {
            sb.AppendLine($"  [{layout.Index}] {layout.LayoutName}");
            sb.AppendLine($"      Section:    {layout.SectionName}");
            sb.AppendLine($"      Descriptor: {layout.DescriptorPath}");
            sb.AppendLine($"      HasStamp:   {layout.HasStamp}");
            sb.AppendLine();
        }

        // ── 3. ZIP Entries Comparison ──────────────────────────────────────
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("3. ZIP ENTRIES");
        sb.AppendLine("───────────────────────────────────────────────────────────────");

        using var originalArchive = ZipFile.OpenRead(originalDwfPath);
        using var stampedArchive = ZipFile.OpenRead(stampedDwfPath);

        var originalEntries = originalArchive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        var stampedEntries = stampedArchive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();

        var addedEntries = stampedEntries.Except(originalEntries).ToList();
        var removedEntries = originalEntries.Except(stampedEntries).ToList();
        var commonEntries = originalEntries.Intersect(stampedEntries).ToList();

        sb.AppendLine($"Original entries: {originalEntries.Count}");
        sb.AppendLine($"Stamped entries:  {stampedEntries.Count}");
        sb.AppendLine();

        if (addedEntries.Count > 0)
        {
            sb.AppendLine($"ADDED ENTRIES ({addedEntries.Count}):");
            foreach (var entry in addedEntries)
            {
                var e = stampedArchive.GetEntry(entry)!;
                sb.AppendLine($"  + {entry}");
                sb.AppendLine($"      Size: {e.Length:N0} bytes | Compressed: {e.CompressedLength:N0} bytes");
            }
            sb.AppendLine();
        }

        if (removedEntries.Count > 0)
        {
            sb.AppendLine($"REMOVED ENTRIES ({removedEntries.Count}):");
            foreach (var entry in removedEntries)
            {
                sb.AppendLine($"  - {entry}");
            }
            sb.AppendLine();
        }

        if (commonEntries.Count > 0)
        {
            sb.AppendLine($"COMMON ENTRIES ({commonEntries.Count}):");
            foreach (var entry in commonEntries)
            {
                var origEntry = originalArchive.GetEntry(entry)!;
                var stampEntry = stampedArchive.GetEntry(entry)!;

                if (origEntry.Length != stampEntry.Length)
                {
                    sb.AppendLine($"  * {entry} (MODIFIED)");
                    sb.AppendLine($"      Original: {origEntry.Length:N0} bytes");
                    sb.AppendLine($"      Stamped:  {stampEntry.Length:N0} bytes");
                }
            }
            sb.AppendLine();
        }

        // ── 4. Manifest.xml Comparison ─────────────────────────────────────
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("4. MANIFEST.XML ANALYSIS");
        sb.AppendLine("───────────────────────────────────────────────────────────────");

        var originalManifest = LoadXml(originalArchive, "manifest.xml");
        var stampedManifest = LoadXml(stampedArchive, "manifest.xml");

        CompareManifests(sb, originalManifest, stampedManifest, "Original", "Stamped");
        sb.AppendLine();

        // ── 5. Descriptor Analysis ─────────────────────────────────────────
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("5. DESCRIPTOR FILES");
        sb.AppendLine("───────────────────────────────────────────────────────────────");

        foreach (var layout in stampedLayouts)
        {
            sb.AppendLine($"Layout [{layout.Index}]: {layout.LayoutName}");
            sb.AppendLine($"  Descriptor: {layout.DescriptorPath}");

            var stampedDescriptor = LoadXml(stampedArchive, layout.DescriptorPath);

            if (layout.Index < originalLayouts.Count)
            {
                var originalDescriptor = LoadXml(originalArchive, originalLayouts[layout.Index].DescriptorPath);
                CompareDescriptors(sb, originalDescriptor, stampedDescriptor, layout.LayoutName);
            }

            sb.AppendLine();
        }

        // ── 6. Full Diagnostic Dumps ───────────────────────────────────────
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("6. FULL DIAGNOSTIC DUMPS");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("ORIGINAL DWF STRUCTURE:");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine(DiagnoseDwf(originalDwfPath));
        sb.AppendLine();

        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("STAMPED DWF STRUCTURE:");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine(DiagnoseDwf(stampedDwfPath));
        sb.AppendLine();

        // ── 7. XML Content (for detailed inspection) ───────────────────────
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("7. FULL XML CONTENT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        sb.AppendLine("─── ORIGINAL MANIFEST.XML ─────────────────────────────────────");
        sb.AppendLine(originalManifest.ToString());
        sb.AppendLine();

        sb.AppendLine("─── STAMPED MANIFEST.XML ──────────────────────────────────────");
        sb.AppendLine(stampedManifest.ToString());
        sb.AppendLine();

        // Write to file
        File.WriteAllText(outputReportPath, sb.ToString(), Encoding.UTF8);
    }

    private static void CompareManifests(
        StringBuilder sb,
        XDocument originalManifest,
        XDocument stampedManifest,
        string originalLabel,
        string stampedLabel)
    {
        var originalSections = GetEplotSections(originalManifest).ToList();
        var stampedSections = GetEplotSections(stampedManifest).ToList();

        sb.AppendLine($"{originalLabel} ePlot sections: {originalSections.Count}");
        sb.AppendLine($"{stampedLabel} ePlot sections:  {stampedSections.Count}");
        sb.AppendLine();

        for (int i = 0; i < Math.Max(originalSections.Count, stampedSections.Count); i++)
        {
            sb.AppendLine($"Section {i}:");

            if (i < originalSections.Count)
            {
                var origSection = originalSections[i];
                var origToc = GetRequiredElement(origSection, DwfNs + "Toc");
                var origResources = origToc.Elements(DwfNs + "Resource").ToList();

                sb.AppendLine($"  {originalLabel}:");
                sb.AppendLine($"    Name: {origSection.Attribute("name")?.Value}");
                sb.AppendLine($"    Resources: {origResources.Count}");
                foreach (var r in origResources)
                {
                    sb.AppendLine($"      - {r.Attribute("role")?.Value}: {r.Attribute("href")?.Value}");
                }
            }

            if (i < stampedSections.Count)
            {
                var stampSection = stampedSections[i];
                var stampToc = GetRequiredElement(stampSection, DwfNs + "Toc");
                var stampResources = stampToc.Elements(DwfNs + "Resource").ToList();

                sb.AppendLine($"  {stampedLabel}:");
                sb.AppendLine($"    Name: {stampSection.Attribute("name")?.Value}");
                sb.AppendLine($"    Resources: {stampResources.Count}");
                foreach (var r in stampResources)
                {
                    sb.AppendLine($"      - {r.Attribute("role")?.Value}: {r.Attribute("href")?.Value}");
                }

                // Highlight added resources
                if (i < originalSections.Count)
                {
                    var origToc = GetRequiredElement(originalSections[i], DwfNs + "Toc");
                    var origRoles = origToc.Elements(DwfNs + "Resource")
                        .Select(r => r.Attribute("role")?.Value ?? "").ToHashSet();
                    var stampRoles = stampResources
                        .Select(r => r.Attribute("role")?.Value ?? "").ToList();

                    var addedRoles = stampRoles.Where(r => !origRoles.Contains(r)).ToList();
                    if (addedRoles.Count > 0)
                    {
                        sb.AppendLine($"    ADDED RESOURCES: {string.Join(", ", addedRoles)}");
                    }
                }
            }

            sb.AppendLine();
        }
    }

    private static void CompareDescriptors(
        StringBuilder sb,
        XDocument originalDescriptor,
        XDocument stampedDescriptor,
        string layoutName)
    {
        var origResources = originalDescriptor.Root?.Element(EPlotNs + "Resources");
        var stampResources = stampedDescriptor.Root?.Element(EPlotNs + "Resources");

        var origCount = origResources?.Elements(EPlotNs + "Resource").Count() ?? 0;
        var stampCount = stampResources?.Elements(EPlotNs + "Resource").Count() ?? 0;

        sb.AppendLine($"  Resources in descriptor:");
        sb.AppendLine($"    Original: {origCount}");
        sb.AppendLine($"    Stamped:  {stampCount}");

        if (stampResources != null)
        {
            sb.AppendLine($"  Stamped descriptor resources:");
            foreach (var r in stampResources.Elements(EPlotNs + "Resource"))
            {
                var role = r.Attribute("role")?.Value;
                var href = r.Attribute("href")?.Value;
                sb.AppendLine($"    - {role}: {href}");
            }
        }
    }
}
