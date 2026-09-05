using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using Google;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Infrastructure.Google.Inspection;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Standalone WPF screenshot host: reads the clipboard on STA, uploads through the shared
/// Google session, and persists attachment metadata directly through the SQL context factory.
/// </summary>
internal sealed class StandaloneInspectionNoteScreenshotHost(
    GoogleInspectionNoteScreenshotUploader uploader,
    IAppLogger? logger = null) : IInspectionNoteScreenshotHost
{
    private readonly GoogleInspectionNoteScreenshotUploader _uploader =
        uploader ?? throw new ArgumentNullException(nameof(uploader));
    private readonly IAppLogger? _logger = logger;

    public async Task<InspectionScreenshotUploadResult> UploadFromClipboardAsync(
        long noteId,
        CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            return InspectionScreenshotUploadResult.Fail("הערה לא חוקית.");

        try
        {
            var pngBytes = await ReadClipboardPngAsync(cancellationToken).ConfigureAwait(true);
            if (pngBytes is null)
            {
                return InspectionScreenshotUploadResult.Fail(
                    "אין תמונה בלוח (Clipboard). העתק/י צילום מסך תחילה.");
            }

            var hash = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();

            var duplicate = await _uploader
                .CheckDuplicateAsync(noteId, hash, cancellationToken)
                .ConfigureAwait(false);

            if (duplicate.DuplicateNoteId == noteId)
                return InspectionScreenshotUploadResult.Fail("התמונה הזו כבר צורפה להערה הזו.");

            if (duplicate.DuplicateNoteId is not null
                && !await ConfirmCrossNoteDuplicateAsync(cancellationToken).ConfigureAwait(false))
            {
                return InspectionScreenshotUploadResult.Fail("העלאה בוטלה.");
            }

            var fileName = $"note-{noteId}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var uploaded = await _uploader
                .UploadAndPersistAsync(
                    noteId,
                    duplicate.ReportId,
                    fileName,
                    hash,
                    pngBytes,
                    cancellationToken)
                .ConfigureAwait(false);

            return InspectionScreenshotUploadResult.Ok(uploaded.GoogleDriveUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ExternalException
                                   or InvalidOperationException
                                   or IOException
                                   or GoogleApiException
                                   or DbException)
        {
            _logger?.Error(
                $"[InspectionScreenshot] Upload failed. NoteId={noteId}",
                ex);
            return InspectionScreenshotUploadResult.Fail($"שגיאה בצירוף צילום מסך: {ex.Message}");
        }
    }

    public async Task<InspectionScreenshotOpenResult> OpenLastAsync(
        long noteId,
        CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            return InspectionScreenshotOpenResult.Fail("הערה לא חוקית.");

        try
        {
            var url = await _uploader
                .GetLastUrlAsync(noteId, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(url))
                return InspectionScreenshotOpenResult.Fail("אין תמונה זמינה לפתיחה (חסר קישור Google Drive).");

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return InspectionScreenshotOpenResult.Ok("נפתחה התמונה האחרונה.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DbException)
        {
            _logger?.Error($"[InspectionScreenshot] Open failed. NoteId={noteId}", ex);
            return InspectionScreenshotOpenResult.Fail($"שגיאה בפתיחת התמונה: {ex.Message}");
        }
    }

    private static async Task<byte[]?> ReadClipboardPngAsync(CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            throw new InvalidOperationException("WPF dispatcher is unavailable.");

        return await dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Clipboard.ContainsImage())
                    return null;

                var image = Clipboard.GetImage();
                if (image is null)
                    return null;

                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
                return stream.ToArray();
            });
    }

    private static async Task<bool> ConfirmCrossNoteDuplicateAsync(CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            throw new InvalidOperationException("WPF dispatcher is unavailable.");

        return await dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MessageBox.Show(
                           "התמונה הזו כבר צורפה לסעיף אחר בדוח. האם לצרף אותה גם לסעיף הנוכחי?",
                           "צירוף צילום מסך — כפילות בדוח",
                           MessageBoxButton.YesNo,
                           MessageBoxImage.Warning,
                           MessageBoxResult.No)
                       == MessageBoxResult.Yes;
            });
    }
}
