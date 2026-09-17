using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Mailbox label table vs SiNet projects, with misplaced move and duplicate merge.</summary>
public sealed class GmailMailboxLabelAuditViewModel : ObservableObject
{
    private readonly IGmailMailboxLabelAuditService? _audit;
    private readonly IEmailGmailModifyService? _modify;
    private string _searchText = string.Empty;
    private string _statusMessage = string.Empty;
    private GmailMailboxLabelAuditRow? _selectedRow;
    private bool _isBusy;
    private IReadOnlyList<GmailMailboxLabelAuditRow> _allRows;

    public GmailMailboxLabelAuditViewModel(
        IReadOnlyList<GmailMailboxLabelAuditRow> rows,
        IGmailMailboxLabelAuditService? audit = null,
        IEmailGmailModifyService? modify = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _allRows = rows;
        _audit = audit;
        _modify = modify;
        FilteredRows = [];
        CopyAllCommand = new RelayCommand(_ => CopyNames(AllRows.Select(static r => r.LabelName)));
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => CanRefresh);
        MoveCommand = new RelayCommand(_ => _ = MoveSelectedAsync(), _ => CanMoveSelected);
        MergeCommand = new RelayCommand(_ => _ = MergeSelectedAsync(), _ => CanMergeSelected);
        ApplyFilter();
    }

    public IReadOnlyList<GmailMailboxLabelAuditRow> AllRows => _allRows;

    public ObservableCollection<GmailMailboxLabelAuditRow> FilteredRows { get; }

    public ICommand CopyAllCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand MoveCommand { get; }

    public ICommand MergeCommand { get; }

    public bool CanRefresh => _audit is not null && !_isBusy;

    public bool CanMoveSelected =>
        !_isBusy
        && _modify is not null
        && SelectedRow is { Status: GmailProjectLabelPathStatus.Misplaced, ExpectedPath: not null };

    public bool CanMergeSelected =>
        !_isBusy
        && _modify is not null
        && SelectedRow is { Status: GmailProjectLabelPathStatus.Duplicate, ParsedProjectNumber: not null };

    public GmailMailboxLabelAuditRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetField(ref _selectedRow, value))
            {
                RaiseActionCanExecute();
            }
        }
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

    public async Task RefreshAsync()
    {
        if (_audit is null || _isBusy)
        {
            return;
        }

        try
        {
            _isBusy = true;
            RaiseActionCanExecute();
            var rows = await _audit.AuditAsync(CancellationToken.None).ConfigureAwait(true);
            ReplaceRows(rows);
            StatusMessage = "התוויות רועננו מ-Gmail.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"רענון נכשל: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            RaiseActionCanExecute();
        }
    }

    public async Task MoveSelectedAsync()
    {
        if (_modify is null || SelectedRow is not { } row
            || row.Status != GmailProjectLabelPathStatus.Misplaced
            || string.IsNullOrWhiteSpace(row.ExpectedPath))
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"העברת התווית למיקום הנכון:{Environment.NewLine}{Environment.NewLine}"
            + $"נוכחי:{Environment.NewLine}{row.LabelName}{Environment.NewLine}{Environment.NewLine}"
            + $"יעד:{Environment.NewLine}{row.ExpectedPath}{Environment.NewLine}{Environment.NewLine}"
            + "אותה תווית תישאר. ההודעות יישארו משויכות.",
            "העבר למיקום הנכון",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _isBusy = true;
            RaiseActionCanExecute();
            var location = EmailProjectLabelParser.TryExtractParentPath(row.ExpectedPath);
            var slash = location?.LastIndexOf('/') ?? -1;
            if (slash >= 0 && location is not null)
            {
                await _modify.EnsureProjectLocationAsync(location[(slash + 1)..], CancellationToken.None)
                    .ConfigureAwait(true);
            }

            await _modify.RenameLabelAsync(row.LabelId, row.ExpectedPath, CancellationToken.None)
                .ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "התווית הועברה למיקום הנכון.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ההעברה נכשלה: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            RaiseActionCanExecute();
        }
    }

    public async Task MergeSelectedAsync()
    {
        if (_modify is null || SelectedRow is not { } source
            || source.ParsedProjectNumber is not int number
            || source.Status != GmailProjectLabelPathStatus.Duplicate)
        {
            return;
        }

        var siblings = AllRows
            .Where(row => row.ParsedProjectNumber == number
                && row.Status == GmailProjectLabelPathStatus.Duplicate
                && !string.Equals(row.LabelId, source.LabelId, StringComparison.Ordinal))
            .ToList();
        if (siblings.Count == 0)
        {
            StatusMessage = "אין תווית יעד למיזוג.";
            return;
        }

        var recommended = siblings.FirstOrDefault(row =>
            !string.IsNullOrWhiteSpace(row.ExpectedPath)
            && string.Equals(row.LabelName, row.ExpectedPath, StringComparison.OrdinalIgnoreCase))
            ?? siblings[0];

        var dialog = new GmailProjectLabelMergeDialog(source, siblings, recommended);
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true || dialog.TargetRow is not { } target)
        {
            return;
        }

        try
        {
            _isBusy = true;
            RaiseActionCanExecute();
            var result = await _modify
                .MergeProjectLabelsAsync(source.LabelId, target.LabelId, CancellationToken.None)
                .ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = result.Succeeded
                ? "המיזוג הושלם. תווית המקור נמחקה."
                : result.ErrorMessage ?? "המיזוג נכשל.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"המיזוג נכשל: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            RaiseActionCanExecute();
        }
    }

    private void ReplaceRows(IReadOnlyList<GmailMailboxLabelAuditRow> rows)
    {
        _allRows = rows;
        OnPropertyChanged(nameof(AllRows));
        ApplyFilter();
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
                || Contains(row.ExpectedPath, needle)
                || Contains(row.StatusLabel, needle)
                || Contains(row.ParsedProjectNumber?.ToString(), needle));
        }

        foreach (var row in source)
        {
            FilteredRows.Add(row);
        }

        var duplicateCount = AllRows.Count(static r => r.Status == GmailProjectLabelPathStatus.Duplicate);
        var misplacedCount = AllRows.Count(static r => r.Status == GmailProjectLabelPathStatus.Misplaced);
        StatusMessage = $"{FilteredRows.Count} תוויות מוצגות (מתוך {AllRows.Count})"
            + (duplicateCount > 0 ? $" · {duplicateCount} בכפילות" : string.Empty)
            + (misplacedCount > 0 ? $" · {misplacedCount} במיקום שגוי" : string.Empty);
    }

    private void RaiseActionCanExecute()
    {
        (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MergeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanMoveSelected));
        OnPropertyChanged(nameof(CanMergeSelected));
    }

    private static bool Contains(string? value, string needle) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
