using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.MasterPlan;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public sealed class MasterPlanMappingViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IMasterPlanMappingService _mappingService;
    private MasterPlanMappingLoadResult? _lastLoad;
    private bool _isBusy;
    private bool _showInactive = true;
    private string _statusMessage = string.Empty;

    public MasterPlanMappingViewModel(IMasterPlanMappingService mappingService)
    {
        _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
        LoadCommand = new RelayCommand(async () => await LoadAsync().ConfigureAwait(true), () => !IsBusy);
        AutoMatchCommand = new RelayCommand(AutoMatch, () => !IsBusy && _lastLoad is not null);
        ApplyCommand = new RelayCommand(async () => await ApplyAsync().ConfigureAwait(true), () => !IsBusy);
        CompleteMissingCommand = new RelayCommand(async () => await CompleteMissingAsync().ConfigureAwait(true), () => !IsBusy);
        EnableFullSyncCommand = new RelayCommand(async () => await EnableFullSyncAsync().ConfigureAwait(true), () => !IsBusy);
        ClearAllCommand = new RelayCommand(ClearAll, () => !IsBusy);
        ExportJsonCommand = new RelayCommand(ExportJson, () => !IsBusy);
        ImportJsonCommand = new RelayCommand(ImportJson, () => !IsBusy);
        ToggleShowInactiveCommand = new RelayCommand(ToggleShowInactive, () => !IsBusy);
    }

    public ObservableCollection<CompanyMappingRow> CompanyRows { get; } = [];
    public ObservableCollection<ContactMappingRow> ContactRows { get; } = [];

    public ICommand LoadCommand { get; }
    public ICommand AutoMatchCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CompleteMissingCommand { get; }
    public ICommand EnableFullSyncCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ImportJsonCommand { get; }
    public ICommand ToggleShowInactiveCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool ShowInactive
    {
        get => _showInactive;
        private set => SetField(ref _showInactive, value);
    }

    public async Task InitializeAsync() => await LoadAsync().ConfigureAwait(true);

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _lastLoad = await _mappingService.LoadAsync().ConfigureAwait(true);
            RebuildRows(_lastLoad);
            StatusMessage = string.IsNullOrWhiteSpace(_lastLoad.Warning)
                ? $"נטענו {CompanyRows.Count} חברות ו-{ContactRows.Count} אנשי קשר."
                : _lastLoad.Warning;
        }
        catch (Exception ex)
        {
            StatusMessage = $"טעינה נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AutoMatch()
    {
        if (_lastLoad is null)
        {
            return;
        }

        var current = CaptureCurrentAsLoadResult();
        var matched = MasterPlanAutoMatchEngine.Apply(current);
        _lastLoad = matched with
        {
            MpCompanies = _lastLoad.MpCompanies,
            MpContacts = _lastLoad.MpContacts,
            Warning = _lastLoad.Warning,
        };
        RebuildRows(_lastLoad);
        StatusMessage = "בוצעה התאמה אוטומטית בזיכרון — לחץ שמירה כדי להחיל.";
    }

    private async Task ApplyAsync()
    {
        IsBusy = true;
        try
        {
            var command = new MasterPlanMappingApplyCommand(
                CompanyRows.Select(r => r.ToChange()).ToList(),
                ContactRows.Select(r => r.ToChange()).ToList());
            var result = await _mappingService.ApplyAsync(command).ConfigureAwait(true);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await LoadAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"שמירה נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompleteMissingAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _mappingService.CompleteMissingAsync().ConfigureAwait(true);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await LoadAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"השלמת חוסרים נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnableFullSyncAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _mappingService.EnableFullSyncAsync().ConfigureAwait(true);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Enable Full Sync נכשל: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearAll()
    {
        foreach (var row in CompanyRows)
        {
            row.MasterPlanCompanyId = null;
            row.SelectedOption = null;
            row.MatchStatus = null;
            row.IsAutoMatch = false;
        }

        foreach (var row in ContactRows)
        {
            row.MasterPlanContactId = null;
            row.SelectedOption = null;
            row.MatchStatus = null;
            row.IsAutoMatch = false;
        }

        StatusMessage = "נוקו כל המיפויים בזיכרון — לחץ שמירה כדי להחיל.";
    }

    private void ExportJson()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"MasterPlanMapping_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var export = new MasterPlanMappingExportDto(
            DateTime.UtcNow,
            CompanyRows
                .Where(r => r.MasterPlanCompanyId is not null)
                .Select(r => new MasterPlanMappingPair(r.SiNetId, r.MasterPlanCompanyId!.Value))
                .ToList(),
            ContactRows
                .Where(r => r.MasterPlanContactId is not null)
                .Select(r => new MasterPlanMappingPair(r.SiNetId, r.MasterPlanContactId!.Value))
                .ToList());

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, JsonOptions));
        StatusMessage = $"יוצא ל-{dialog.FileName}";
    }

    private void ImportJson()
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var export = JsonSerializer.Deserialize<MasterPlanMappingExportDto>(json, JsonOptions);
            if (export is null)
            {
                StatusMessage = "קובץ JSON לא תקין.";
                return;
            }

            var companyMap = export.Companies.ToDictionary(p => p.SiNetId, p => p.MasterPlanId);
            var contactMap = export.Contacts.ToDictionary(p => p.SiNetId, p => p.MasterPlanId);

            foreach (var row in CompanyRows)
            {
                if (companyMap.TryGetValue(row.SiNetId, out var mpId))
                {
                    row.MasterPlanCompanyId = mpId;
                    row.MatchStatus = "ייבוא";
                    row.IsAutoMatch = false;
                    row.SelectedOption = row.MpOptions.FirstOrDefault(o => o.Id == mpId);
                }
            }

            foreach (var row in ContactRows)
            {
                if (contactMap.TryGetValue(row.SiNetId, out var mpId))
                {
                    row.MasterPlanContactId = mpId;
                    row.MatchStatus = "ייבוא";
                    row.IsAutoMatch = false;
                    row.SelectedOption = row.MpOptions.FirstOrDefault(o => o.Id == mpId);
                }
            }

            StatusMessage = "ייבוא הושלם בזיכרון — לחץ שמירה כדי להחיל.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ייבוא נכשל: {ex.Message}";
        }
    }

    private void ToggleShowInactive()
    {
        ShowInactive = !ShowInactive;
        if (_lastLoad is not null)
        {
            RebuildRows(CaptureCurrentAsLoadResult() with
            {
                MpCompanies = _lastLoad.MpCompanies,
                MpContacts = _lastLoad.MpContacts,
                Warning = _lastLoad.Warning,
            });
        }
    }

    private void RebuildRows(MasterPlanMappingLoadResult load)
    {
        CompanyRows.Clear();
        ContactRows.Clear();

        foreach (var dto in load.Companies)
        {
            if (!ShowInactive && !dto.IsActive)
            {
                continue;
            }

            CompanyRows.Add(CompanyMappingRow.FromDto(dto, load.MpCompanies));
        }

        foreach (var dto in load.Contacts)
        {
            if (!ShowInactive && !dto.IsActive)
            {
                continue;
            }

            ContactRows.Add(ContactMappingRow.FromDto(dto, load.MpContacts));
        }
    }

    private MasterPlanMappingLoadResult CaptureCurrentAsLoadResult()
    {
        var companies = CompanyRows.Select(r => new MasterPlanCompanyMappingDto(
            r.SiNetId,
            r.SiNetTitle,
            r.SiNetEmail,
            r.SiNetPhone,
            r.ProjectCount,
            r.ContactCount,
            r.IsActive,
            r.MasterPlanCompanyId,
            r.MatchStatus,
            r.IsAutoMatch)).ToList();

        var contacts = ContactRows.Select(r => new MasterPlanContactMappingDto(
            r.SiNetId,
            r.SiNetFullName,
            r.SiNetCompanyId,
            r.SiNetCompanyTitle,
            r.SiNetEmail,
            r.SiNetPhone,
            r.ProjectCount,
            r.IsActive,
            r.MasterPlanContactId,
            r.MatchStatus,
            r.IsAutoMatch)).ToList();

        return new MasterPlanMappingLoadResult(
            companies,
            contacts,
            _lastLoad?.MpCompanies ?? [],
            _lastLoad?.MpContacts ?? [],
            _lastLoad?.Warning);
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task>? _asyncExecute;
        private readonly Action? _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Func<Task> asyncExecute, Func<bool>? canExecute = null)
        {
            _asyncExecute = asyncExecute ?? throw new ArgumentNullException(nameof(asyncExecute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public async void Execute(object? parameter)
        {
            if (_asyncExecute is not null)
            {
                await _asyncExecute().ConfigureAwait(true);
                return;
            }

            _execute?.Invoke();
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
