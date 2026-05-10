using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Windows;

public partial class ReviewedPlanPickerWindow : Window
{
    private readonly ObservableCollection<Row> _rows;

    internal ReviewedPlanPickerWindow(
        IReadOnlyList<ReviewedPlanCandidate> candidates,
        HashSet<(string FileName, string Alternative)> preselected)
    {
        InitializeComponent();

        _rows = new ObservableCollection<Row>(
            candidates
                .OrderBy(c => c.FileName)
                .ThenBy(c => c.Alternative)
                .Select(c => new Row
                {
                    FileName = c.FileName,
                    Alternative = c.Alternative ?? string.Empty,
                    IsSelected = preselected.Contains((c.FileName, c.Alternative ?? string.Empty))
                }));

        CandidatesList.ItemsSource = _rows;
    }

    public IReadOnlyList<ReviewedPlanCandidate> SelectedCandidates =>
        _rows.Where(r => r.IsSelected)
             .Select(r => new ReviewedPlanCandidate(r.FileName, r.Alternative))
             .ToList();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private sealed class Row : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public string FileName { get; set; } = string.Empty;
        public string Alternative { get; set; } = string.Empty;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
