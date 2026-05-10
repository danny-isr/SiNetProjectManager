using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SiNetSQL.Services.ActiveFileQuery;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services.Inspection;

/// <summary>
/// WPF implementation of <see cref="IReviewedPlanPicker"/>. Delegates to the
/// unified <see cref="FileTreePicker"/> in Multiple mode so the reviewed-plans
/// flow uses the same tree UI as the note-linked-file flow.
/// </summary>
internal sealed class ReviewedPlanPicker : IReviewedPlanPicker
{
    private readonly IFileTreePicker _treePicker;

    public ReviewedPlanPicker(IFileTreePicker treePicker)
    {
        _treePicker = treePicker;
    }

    public ReviewedPlanPicker() : this(new FileTreePicker()) { }

    public async Task<IReadOnlyList<ReviewedPlanCandidate>?> PickAsync(
        IReadOnlyList<ReviewedPlanCandidate> candidates,
        IReadOnlyList<ReviewedPlanCandidate> alreadySelected,
        CancellationToken cancellationToken = default)
    {
        // Source of truth: the live work-window tree. We deliberately do NOT
        // synthesize a virtual tree from the flat candidate list anymore —
        // that produced "theoretical" entries with no existing file.
        IReadOnlyList<ActiveFolderInfo> tree = ActiveFileQueryRegistry.Instance.GetActiveFolderTree();

        var preset = alreadySelected
            .Select(c => new FilePickerSelection(c.FileName, c.Alternative ?? string.Empty))
            .ToList();

        var picked = await _treePicker.PickAsync(new FileTreePickerRequest
        {
            Title = "בחר תוכניות נבדקות",
            Purpose = "ReviewedPlans",
            SelectionMode = FilePickerSelectionMode.Multiple,
            Tree = tree,
            AlreadySelected = preset
        }, cancellationToken).ConfigureAwait(true);

        if (picked == null) return null;
        return picked
            .Select(p => new ReviewedPlanCandidate(p.FileName, p.Alternative))
            .ToList();
    }
}
