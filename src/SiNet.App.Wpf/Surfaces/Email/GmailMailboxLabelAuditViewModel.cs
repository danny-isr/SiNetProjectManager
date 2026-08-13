using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Email;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Read-only sortable table of mailbox user labels vs SiNet projects (DEV-026).</summary>
public sealed class GmailMailboxLabelAuditViewModel : ObservableObject
{
    private string _searchText = string.Empty;
    private string _statusMessage = string.Empty;
    private GmailMailboxLabelAuditRow? _selectedRow;

    public GmailMailboxLabelAuditViewModel(IReadOnlyList<GmailMailboxLabelAuditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        AllRows = rows;
        FilteredRows = [];
        CopyAllCommand = new RelayCommand(_ => CopyNames(AllRows.Select(static r => r.LabelName)));
        ApplyFilter();
    }

    public IReadOnlyList<GmailMailboxLabelAuditRow> AllRows { get; }

    public ObservableCollection<GmailMailboxLabelAuditRow> FilteredRows { get; }

    public ICommand CopyAllCommand { get; }

    public GmailMailboxLabelAuditRow? SelectedRow
    {
        get => _selectedRow;
        set => SetField(ref _selectedRow, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public void CopyNames(IEnumerable<string> names)
    {
        var text = string.Join(Environment.NewLine, names.Where(static n => !string.IsNullOrWhiteSpace(n)));
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "אין שמות להעתקה.";
            return;
        }

        Clipboard.SetText(text);
        StatusMessage = "הועתקו שמות התוויות.";
    }

    private void ApplyFilter()
    {
        FilteredRows.Clear();
        var needle = SearchText.Trim();
        IEnumerable<GmailMailboxLabelAuditRow> source = AllRows;
        if (!string.IsNullOrEmpty(needle))
        {
            source = AllRows.Where(row =>
                Contains(row.LabelName, needle)
                || Contains(row.ProjectDisplayName, needle)
                || Contains(row.PlaceName, needle)
                || Contains(row.Note, needle)
                || Contains(row.ParsedProjectNumber?.ToString(), needle));
        }

        foreach (var row in source)
        {
            FilteredRows.Add(row);
        }

        var duplicateCount = AllRows.Count(static r => r.IsDuplicate);
        StatusMessage = duplicateCount > 0
            ? $"{FilteredRows.Count} תוויות מוצגות (מתוך {AllRows.Count}) · {duplicateCount} בכפילות"
            : $"{FilteredRows.Count} תוויות מוצגות (מתוך {AllRows.Count})";
    }

    private static bool Contains(string? value, string needle) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
