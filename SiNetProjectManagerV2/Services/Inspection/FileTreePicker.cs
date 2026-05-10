using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SiNetProjectManagerV2.Windows;
using SiNetSQL.Services;
using SiNetSQL.Services.ActiveFileQuery;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services.Inspection;

/// <summary>
/// WPF implementation of <see cref="IFileTreePicker"/>. Shows a hierarchical
/// TreeView (folder → file → alternative) sourced from the live work-window
/// tree via <c>ActiveFileQueryRegistry</c>. The same picker serves both the
/// reviewed-plans flow (Multiple) and the per-note linked-file flow (Single).
/// Only files/alternatives that have at least one existing version (on disk
/// or in ACC) are displayed and selectable.
/// </summary>
internal sealed class FileTreePicker : IFileTreePicker
{
    public Task<IReadOnlyList<FilePickerSelection>?> PickAsync(
        FileTreePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FilePickerSelection>? result = null;
        FileTreePickerWindow.FilterStats? stats = null;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            (result, stats) = ShowDialog(request);
        }
        else
        {
            dispatcher.Invoke(() => { (result, stats) = ShowDialog(request); });
        }

        var reason = result == null
            ? (stats != null && stats.SelectableLeafCount == 0
                ? " Reason=NoExistingFilesAvailable"
                : string.Empty)
            : string.Empty;

        AppLogger.Debug(
            $"[FileTreePicker] Operation=FileTreePicker " +
            $"PickerPurpose={request.Purpose} SelectionMode={request.SelectionMode} " +
            $"Source=ActiveFolderTree " +
            $"TotalNodesBeforeFilter={stats?.TotalFiles ?? 0} " +
            $"AvailableFileCount={stats?.AvailableFiles ?? 0} " +
            $"SelectableFileCount={stats?.SelectableLeafCount ?? 0} " +
            $"HiddenMissingFileCount={stats?.HiddenMissingFiles ?? 0} " +
            $"DisplayedFolderCount={stats?.DisplayedFolders ?? 0} " +
            $"DisplayedFileCount={stats?.DisplayedFiles ?? 0} " +
            $"Result={(result == null ? "Cancelled" : "Confirmed")}" + reason);

        return Task.FromResult(result);
    }

    private static (IReadOnlyList<FilePickerSelection>? Selection, FileTreePickerWindow.FilterStats Stats)
        ShowDialog(FileTreePickerRequest request)
    {
        var window = new FileTreePickerWindow(request)
        {
            Title = request.Title,
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow
        };
        var ok = window.ShowDialog() == true;
        return (ok ? window.Selected : null, window.Stats);
    }
}
