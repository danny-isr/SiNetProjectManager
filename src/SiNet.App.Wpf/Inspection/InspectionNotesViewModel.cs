using System.Collections.ObjectModel;
using SiNet.App.Wpf.Infrastructure;
using SiNet.Application.Abstractions.Inspection;

namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// View model for the Inspection notes area. Read-only: shows the notes under the report selected in
/// the tree, loaded through the clean <see cref="IInspectionWorkspace"/> port. Note editing, ordering,
/// status flow and creation stay in the legacy window. Empty when no report is selected.
/// </summary>
public sealed class InspectionNotesViewModel : ObservableObject
{
    private readonly IInspectionWorkspace _workspace;
    private bool _isLoading;
    private string? _errorMessage;

    public InspectionNotesViewModel(IInspectionWorkspace workspace)
    {
        _workspace = workspace;
    }

    public string Title => "Notes";

    public ObservableCollection<InspectionNoteRow> Notes { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>Loads read-only notes for a report. Clears the list when <paramref name="reportId"/> is null.</summary>
    public async Task LoadNotesAsync(int? reportId, CancellationToken cancellationToken = default)
    {
        Notes.Clear();
        ErrorMessage = null;
        if (reportId is not int id || id <= 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var notes = await _workspace.GetNotesAsync(id, cancellationToken).ConfigureAwait(true);
            foreach (var note in notes)
            {
                Notes.Add(note);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(LoadNotesAsync));
        }
        finally
        {
            IsLoading = false;
        }
    }
}
