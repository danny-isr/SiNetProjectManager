using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Lets the user keep one Gmail project leaf and delete other leaves that share the same
/// <c>(Number)</c> (DEV-009 Layer B). No silent merge.
/// </summary>
public partial class GmailDuplicateLabelDecisionDialog : Window, INotifyPropertyChanged
{
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public GmailDuplicateLabelDecisionDialog(
        IReadOnlyList<ProjectGmailLabelSyncItem> duplicateItems)
    {
        ArgumentNullException.ThrowIfNull(duplicateItems);

        Groups = new ObservableCollection<DuplicateNumberGroup>(
            duplicateItems
                .GroupBy(i => i.ProjectNumber)
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var candidates = g
                        .Select(i => new DuplicateLabelCandidate(i.LabelId, i.CurrentFullPath, i.LeafName))
                        .ToList();
                    return new DuplicateNumberGroup(g.Key, candidates)
                    {
                        SelectedKeep = candidates[0],
                    };
                }));

        if (Groups.Count == 0)
            throw new ArgumentException("No duplicate (Number) groups to resolve.", nameof(duplicateItems));

        InitializeComponent();
        DataContext = this;
        ConfirmCommand = new RelayCommand(_ => Confirm(), _ => CanConfirm());
        CancelCommand = new RelayCommand(_ =>
        {
            DialogResult = false;
            Close();
        });
    }

    public ObservableCollection<DuplicateNumberGroup> Groups { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Chosen keep label id per project number after a successful OK.</summary>
    public IReadOnlyDictionary<int, string> KeepSelections { get; private set; }
        = new Dictionary<int, string>();

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool CanConfirm() =>
        !IsBusy && Groups.All(g => g.SelectedKeep is not null);

    private void Confirm()
    {
        if (!CanConfirm())
        {
            StatusMessage = "יש לבחור לייבל אחד לשמירה בכל קבוצה.";
            return;
        }

        KeepSelections = Groups.ToDictionary(
            g => g.ProjectNumber,
            g => g.SelectedKeep!.LabelId);
        DialogResult = true;
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public sealed class DuplicateNumberGroup : INotifyPropertyChanged
    {
        private DuplicateLabelCandidate? _selectedKeep;

        public DuplicateNumberGroup(int projectNumber, IReadOnlyList<DuplicateLabelCandidate> candidates)
        {
            ProjectNumber = projectNumber;
            Candidates = candidates;
            Header = $"מספר פרויקט ({projectNumber}) — {candidates.Count} לייבלים";
        }

        public int ProjectNumber { get; }

        public string Header { get; }

        public IReadOnlyList<DuplicateLabelCandidate> Candidates { get; }

        public DuplicateLabelCandidate? SelectedKeep
        {
            get => _selectedKeep;
            set
            {
                if (ReferenceEquals(_selectedKeep, value)) return;
                _selectedKeep = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedKeep)));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed record DuplicateLabelCandidate(string LabelId, string FullPath, string LeafName);

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
