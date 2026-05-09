using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SiNetProjectManagerV2.Windows;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services.Inspection;

/// <summary>
/// WPF implementation of <see cref="IReviewedPlanPicker"/>. Shows a simple
/// modal list of (FileName, Alternative) candidates so the user can confirm
/// which logical files were the reviewed plan for the selected report.
/// Source candidates are passed in by <c>FloatingInspectionViewModel</c>
/// from <c>ActiveFileQueryRegistry</c>; this picker never scans the filesystem.
/// </summary>
internal sealed class ReviewedPlanPicker : IReviewedPlanPicker
{
    public Task<IReadOnlyList<ReviewedPlanCandidate>?> PickAsync(
        IReadOnlyList<ReviewedPlanCandidate> candidates,
        IReadOnlyList<ReviewedPlanCandidate> alreadySelected,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewedPlanCandidate>? result = null;

        var ownerThread = Application.Current?.Dispatcher;
        if (ownerThread is null || ownerThread.CheckAccess())
        {
            result = ShowDialog(candidates, alreadySelected);
        }
        else
        {
            ownerThread.Invoke(() =>
            {
                result = ShowDialog(candidates, alreadySelected);
            });
        }

        return Task.FromResult(result);
    }

    private static IReadOnlyList<ReviewedPlanCandidate>? ShowDialog(
        IReadOnlyList<ReviewedPlanCandidate> candidates,
        IReadOnlyList<ReviewedPlanCandidate> alreadySelected)
    {
        var preselected = new HashSet<(string, string)>(
            alreadySelected.Select(c => (c.FileName, c.Alternative ?? string.Empty)));

        var window = new ReviewedPlanPickerWindow(candidates, preselected)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow
        };

        return window.ShowDialog() == true ? window.SelectedCandidates : null;
    }
}
