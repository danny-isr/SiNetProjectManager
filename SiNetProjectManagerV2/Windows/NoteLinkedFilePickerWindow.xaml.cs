using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Windows;

public partial class NoteLinkedFilePickerWindow : Window
{
    private readonly List<Row> _all;
    private readonly ObservableCollection<Row> _view;

    internal NoteLinkedFilePickerWindow(
        IReadOnlyList<ReviewedPlanCandidate> candidates,
        ReviewedPlanCandidate? currentSelection)
    {
        InitializeComponent();

        _all = candidates
            .OrderBy(c => c.FileName)
            .ThenBy(c => c.Alternative)
            .Select(c => new Row { FileName = c.FileName, Alternative = c.Alternative ?? string.Empty })
            .ToList();

        _view = new ObservableCollection<Row>(_all);
        CandidatesList.ItemsSource = _view;

        if (currentSelection != null)
        {
            var preset = _view.FirstOrDefault(r =>
                r.FileName == currentSelection.FileName &&
                r.Alternative == (currentSelection.Alternative ?? string.Empty));
            if (preset != null)
            {
                CandidatesList.SelectedItem = preset;
                CandidatesList.ScrollIntoView(preset);
            }
        }
    }

    public ReviewedPlanCandidate? Selected
    {
        get
        {
            if (CandidatesList.SelectedItem is Row r)
                return new ReviewedPlanCandidate(r.FileName, r.Alternative);
            return null;
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        var q = FilterBox.Text?.Trim() ?? string.Empty;
        _view.Clear();
        IEnumerable<Row> src = _all;
        if (q.Length > 0)
            src = src.Where(r =>
                (r.FileName?.Contains(q, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Alternative?.Contains(q, System.StringComparison.OrdinalIgnoreCase) ?? false));
        foreach (var r in src) _view.Add(r);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (CandidatesList.SelectedItem == null)
        {
            MessageBox.Show("יש לבחור קובץ מהרשימה.", "בחירת קובץ מקושר",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void OnListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CandidatesList.SelectedItem != null)
        {
            DialogResult = true;
            Close();
        }
    }

    private sealed class Row
    {
        public string FileName { get; set; } = string.Empty;
        public string Alternative { get; set; } = string.Empty;
    }
}
