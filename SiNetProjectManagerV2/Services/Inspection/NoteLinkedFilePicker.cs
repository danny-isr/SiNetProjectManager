using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SiNetSQL.Services.ActiveFileQuery;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services.Inspection;

/// <summary>
/// WPF implementation of <see cref="INoteLinkedFilePicker"/>. Delegates to the
/// unified <see cref="FileTreePicker"/> in Single mode so per-note linked-file
/// selection shares the same tree UI as the reviewed-plans picker.
/// </summary>
internal sealed class NoteLinkedFilePicker : INoteLinkedFilePicker
{
    private readonly IFileTreePicker _treePicker;

    public NoteLinkedFilePicker(IFileTreePicker treePicker)
    {
        _treePicker = treePicker;
    }

    public NoteLinkedFilePicker() : this(new FileTreePicker()) { }

    public async Task<ReviewedPlanCandidate?> PickAsync(
        IReadOnlyList<ReviewedPlanCandidate> candidates,
        ReviewedPlanCandidate? currentSelection,
        CancellationToken cancellationToken = default)
    {
        // Source of truth: the live work-window tree. No virtual fallback —
        // we never want to show theoretical entries that have no real file.
        IReadOnlyList<ActiveFolderInfo> tree = ActiveFileQueryRegistry.Instance.GetActiveFolderTree();

        var preset = currentSelection == null
            ? System.Array.Empty<FilePickerSelection>()
            : new[] { new FilePickerSelection(currentSelection.FileName, currentSelection.Alternative ?? string.Empty) };

        var picked = await _treePicker.PickAsync(new FileTreePickerRequest
        {
            Title = "בחר קובץ מקושר להערה",
            Purpose = "NoteLinkedFile",
            SelectionMode = FilePickerSelectionMode.Single,
            Tree = tree,
            AlreadySelected = preset
        }, cancellationToken).ConfigureAwait(true);

        if (picked == null || picked.Count == 0) return null;
        var p = picked[0];
        return new ReviewedPlanCandidate(p.FileName, p.Alternative);
    }
}
