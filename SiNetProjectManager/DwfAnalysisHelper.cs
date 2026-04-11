using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SiNetProjectManager.Services.Stamping;

namespace SiNetProjectManager;

/// <summary>
/// Helper utility for analyzing DWF files.
/// Quick way to compare original vs. stamped DWF and generate analysis report.
/// </summary>
public static class DwfAnalysisHelper
{
    /// <summary>
    /// Opens file dialogs to select two DWF files and generates a comparison report.
    /// Call this from Immediate Window: DwfAnalysisHelper.RunInteractiveComparison();
    /// </summary>
    public static void RunInteractiveComparison()
    {
        try
        {
            // Select original DWF
            var originalDialog = new OpenFileDialog
            {
                Title = "בחר קובץ DWF מקורי (ללא חותמת)",
                Filter = "DWF Files (*.dwf)|*.dwf|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (originalDialog.ShowDialog() != true)
            {
                MessageBox.Show("נדרש לבחור קובץ מקורי.", "ביטול", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var originalPath = originalDialog.FileName;

            // Select stamped DWF
            var stampedDialog = new OpenFileDialog
            {
                Title = "בחר קובץ DWF חתום (עם חותמת)",
                Filter = "DWF Files (*.dwf)|*.dwf|All Files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = Path.GetDirectoryName(originalPath)
            };

            if (stampedDialog.ShowDialog() != true)
            {
                MessageBox.Show("נדרש לבחור קובץ חתום.", "ביטול", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var stampedPath = stampedDialog.FileName;

            // Generate report path
            var reportPath = Path.Combine(
                Path.GetDirectoryName(originalPath)!,
                $"DWF_Analysis_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            // Run analysis
            MessageBox.Show(
                $"מנתח קבצים...\n\n" +
                $"מקורי: {Path.GetFileName(originalPath)}\n" +
                $"חתום: {Path.GetFileName(stampedPath)}\n\n" +
                $"הדוח יישמר ב:\n{reportPath}",
                "ניתוח DWF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DwfStampManager.CompareAndGenerateReport(originalPath, stampedPath, reportPath);

            // Show success and offer to open
            var result = MessageBox.Show(
                $"✅ הניתוח הושלם!\n\n" +
                $"הדוח נשמר ב:\n{reportPath}\n\n" +
                $"האם לפתוח את הדוח?",
                "הצלחה",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"❌ שגיאה בניתוח:\n\n{ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Compares THREE DWF files: original, Design Review stamped (working), and our code's output (broken).
    /// Generates a detailed 3-way comparison report.
    /// </summary>
    public static void RunThreeWayComparison()
    {
        try
        {
            // 1. Select ORIGINAL DWF (no stamp)
            var originalDialog = new OpenFileDialog
            {
                Title = "קובץ 1: ORIGINAL - קובץ מקורי ללא חותמת",
                Filter = "DWF Files (*.dwf)|*.dwf|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (originalDialog.ShowDialog() != true)
            {
                MessageBox.Show("נדרש לבחור קובץ מקורי.", "ביטול", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var originalPath = originalDialog.FileName;
            var baseDir = Path.GetDirectoryName(originalPath)!;

            // 2. Select DESIGN REVIEW stamped DWF (working, created by Design Review)
            var designReviewDialog = new OpenFileDialog
            {
                Title = "קובץ 2: DESIGN REVIEW - קובץ עם חותמת מ-Design Review (עובד)",
                Filter = "DWF Files (*.dwf)|*.dwf|All Files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = baseDir
            };

            if (designReviewDialog.ShowDialog() != true)
            {
                MessageBox.Show("נדרש לבחור קובץ Design Review.", "ביטול", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var designReviewPath = designReviewDialog.FileName;

            // 3. Select OUR CODE's stamped DWF (broken, created by our code)
            var ourCodeDialog = new OpenFileDialog
            {
                Title = "קובץ 3: OUR CODE - קובץ שהקוד שלנו יצר (לא עובד)",
                Filter = "DWF Files (*.dwf)|*.dwf|All Files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = baseDir
            };

            if (ourCodeDialog.ShowDialog() != true)
            {
                MessageBox.Show("נדרש לבחור קובץ הקוד שלנו.", "ביטול", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ourCodePath = ourCodeDialog.FileName;

            // Generate report path
            var reportPath = Path.Combine(baseDir, $"DWF_3Way_Analysis_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            MessageBox.Show(
                $"מנתח 3 קבצים...\n\n" +
                $"1. Original: {Path.GetFileName(originalPath)}\n" +
                $"2. Design Review: {Path.GetFileName(designReviewPath)}\n" +
                $"3. Our Code: {Path.GetFileName(ourCodePath)}\n\n" +
                $"הדוח יישמר ב:\n{reportPath}",
                "ניתוח תלת-כיווני",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            CompareThreeFiles(originalPath, designReviewPath, ourCodePath, reportPath);

            var result = MessageBox.Show(
                $"✅ הניתוח הושלם!\n\n" +
                $"הדוח נשמר ב:\n{reportPath}\n\n" +
                $"האם לפתוח את הדוח?",
                "הצלחה",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"❌ שגיאה בניתוח:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Analyzes two specific DWF files without UI.
    /// </summary>
    public static void CompareFiles(string originalPath, string stampedPath, string? outputPath = null)
    {
        outputPath ??= Path.Combine(
            Path.GetDirectoryName(originalPath)!,
            $"DWF_Analysis_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        DwfStampManager.CompareAndGenerateReport(originalPath, stampedPath, outputPath);

        Console.WriteLine($"Analysis complete: {outputPath}");
    }

    /// <summary>
    /// Generates a 3-way comparison report.
    /// </summary>
    private static void CompareThreeFiles(string originalPath, string designReviewPath, string ourCodePath, string reportPath)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("  3-WAY DWF COMPARISON REPORT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"ORIGINAL (no stamp):      {Path.GetFileName(originalPath)}");
        sb.AppendLine($"DESIGN REVIEW (working):  {Path.GetFileName(designReviewPath)}");
        sb.AppendLine($"OUR CODE (broken):        {Path.GetFileName(ourCodePath)}");
        sb.AppendLine();

        // Magic Headers
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("1. MAGIC HEADERS");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"Original:       {DwfStampManager.VerifyMagicHeader(originalPath)}");
        sb.AppendLine($"Design Review:  {DwfStampManager.VerifyMagicHeader(designReviewPath)}");
        sb.AppendLine($"Our Code:       {DwfStampManager.VerifyMagicHeader(ourCodePath)}");
        sb.AppendLine();

        // Layouts
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("2. LAYOUTS");
        sb.AppendLine("───────────────────────────────────────────────────────────────");

        var originalLayouts = DwfStampManager.GetLayouts(originalPath);
        var designReviewLayouts = DwfStampManager.GetLayouts(designReviewPath);
        var ourCodeLayouts = DwfStampManager.GetLayouts(ourCodePath);

        sb.AppendLine($"Original layouts:       {originalLayouts.Count}");
        sb.AppendLine($"Design Review layouts:  {designReviewLayouts.Count}");
        sb.AppendLine($"Our Code layouts:       {ourCodeLayouts.Count}");
        sb.AppendLine();

        for (int i = 0; i < Math.Max(Math.Max(originalLayouts.Count, designReviewLayouts.Count), ourCodeLayouts.Count); i++)
        {
            sb.AppendLine($"Layout [{i}]:");

            if (i < originalLayouts.Count)
            {
                var l = originalLayouts[i];
                sb.AppendLine($"  Original:       {l.LayoutName} | Section: {l.SectionName} | HasStamp: {l.HasStamp}");
            }

            if (i < designReviewLayouts.Count)
            {
                var l = designReviewLayouts[i];
                sb.AppendLine($"  Design Review:  {l.LayoutName} | Section: {l.SectionName} | HasStamp: {l.HasStamp}");
            }

            if (i < ourCodeLayouts.Count)
            {
                var l = ourCodeLayouts[i];
                sb.AppendLine($"  Our Code:       {l.LayoutName} | Section: {l.SectionName} | HasStamp: {l.HasStamp}");
            }

            sb.AppendLine();
        }

        // Diagnostics
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("3. FULL DIAGNOSTICS");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        sb.AppendLine("─── ORIGINAL ──────────────────────────────────────────────────");
        sb.AppendLine(DwfStampManager.DiagnoseDwf(originalPath));
        sb.AppendLine();

        sb.AppendLine("─── DESIGN REVIEW ─────────────────────────────────────────────");
        sb.AppendLine(DwfStampManager.DiagnoseDwf(designReviewPath));
        sb.AppendLine();

        sb.AppendLine("─── OUR CODE ──────────────────────────────────────────────────");
        sb.AppendLine(DwfStampManager.DiagnoseDwf(ourCodePath));
        sb.AppendLine();

        // Detailed 2-way comparisons
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("4. DETAILED COMPARISONS");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // A vs B (Design Review)
        var tempAB = Path.GetTempFileName();
        DwfStampManager.CompareAndGenerateReport(originalPath, designReviewPath, tempAB);
        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine("ORIGINAL → DESIGN REVIEW (what Design Review did)");
        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine(File.ReadAllText(tempAB));
        sb.AppendLine();

        // A vs C (Our Code)
        var tempAC = Path.GetTempFileName();
        DwfStampManager.CompareAndGenerateReport(originalPath, ourCodePath, tempAC);
        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine("ORIGINAL → OUR CODE (what our code did)");
        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine(File.ReadAllText(tempAC));
        sb.AppendLine();

        // B vs C (Differences)
        var tempBC = Path.GetTempFileName();
        DwfStampManager.CompareAndGenerateReport(designReviewPath, ourCodePath, tempBC);
        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine("DESIGN REVIEW → OUR CODE (differences that break it)");
        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine(File.ReadAllText(tempBC));
        sb.AppendLine();

        File.WriteAllText(reportPath, sb.ToString(), System.Text.Encoding.UTF8);

        // Cleanup
        try
        {
            File.Delete(tempAB);
            File.Delete(tempAC);
            File.Delete(tempBC);
        }
        catch { }
    }
}
