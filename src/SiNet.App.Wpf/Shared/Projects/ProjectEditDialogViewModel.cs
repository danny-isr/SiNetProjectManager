using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Shared.Projects;

public sealed class ProjectJobTypeEditRowViewModel : ObservableObject
{
    private bool _isSelected;
    private UserLookupDto? _adminWorker;
    private string _bidValueText = "0";

    public ProjectJobTypeEditRowViewModel(
        ProjectJobTypeEditLine line,
        IReadOnlyList<UserLookupDto> workers)
    {
        ArgumentNullException.ThrowIfNull(line);
        JobTypeId = line.JobTypeId;
        Title = line.JobTypeTitle;
        Workers = workers;
        _isSelected = line.IsSelected;
        _adminWorker = workers.FirstOrDefault(w => w.UserId == line.AdminWorkerId);
        _bidValueText = line.BidValue.ToString(CultureInfo.CurrentCulture);
    }

    public int JobTypeId { get; }

    public string Title { get; }

    public IReadOnlyList<UserLookupDto> Workers { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public UserLookupDto? AdminWorker
    {
        get => _adminWorker;
        set => SetField(ref _adminWorker, value);
    }

    public string BidValueText
    {
        get => _bidValueText;
        set => SetField(ref _bidValueText, value ?? "0");
    }

    public ProjectJobTypeEditLine ToLine()
    {
        if (!decimal.TryParse(BidValueText, NumberStyles.Number, CultureInfo.CurrentCulture, out var bid)
            && !decimal.TryParse(BidValueText, NumberStyles.Number, CultureInfo.InvariantCulture, out bid))
        {
            bid = 0m;
        }

        return new ProjectJobTypeEditLine(
            JobTypeId,
            Title,
            IsSelected,
            AdminWorker?.UserId,
            bid);
    }
}

/// <summary>View model for the native «עדכון פרויקט» dialog.</summary>
public sealed class ProjectEditDialogViewModel : ObservableObject, IDisposable
{
    private readonly IProjectUpdateService _updateService;
    private readonly IProjectRenameOrchestrator _renameOrchestrator;
    private readonly IPlaceCatalogService _places;
    private readonly ICompanyCatalogService _companies;
    private readonly IProjectQueryService _projectQuery;
    private readonly IProjectFilterOptionsService _filterOptions;
    private readonly IUserLookupService _users;
    private readonly InMemoryCurrentProjectContext _parentProjectContext = new();

    private int _projectId;
    private string _projectNumberDisplay = "—";
    private string _projectTitle = string.Empty;
    private string _renameTitle = string.Empty;
    private PlaceDto? _selectedPlace;
    private CompanyDto? _selectedCompany;
    private ContactDto? _selectedContact;
    private ProjectFilterOptionDto? _selectedStatus;
    private string _approveDescription = string.Empty;
    private string _validationMessage = string.Empty;
    private bool _isBusy;
    private bool _disposed;
    private HashSet<int> _loadedSelectedJobTypeIds = [];

    public ProjectEditDialogViewModel(
        IProjectUpdateService updateService,
        IProjectRenameOrchestrator renameOrchestrator,
        IPlaceCatalogService places,
        ICompanyCatalogService companies,
        IJobTypeQueryService jobTypes,
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptions,
        IUserLookupService users,
        IAppSettingsService? appSettings = null)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _renameOrchestrator = renameOrchestrator ?? throw new ArgumentNullException(nameof(renameOrchestrator));
        _places = places ?? throw new ArgumentNullException(nameof(places));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _projectQuery = projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
        _ = jobTypes ?? throw new ArgumentNullException(nameof(jobTypes));
        _filterOptions = filterOptions ?? throw new ArgumentNullException(nameof(filterOptions));
        _users = users ?? throw new ArgumentNullException(nameof(users));

        ParentProjectSelector = new ProjectSelectorViewModel(
            _projectQuery,
            filterOptions,
            _parentProjectContext,
            appSettings: appSettings);
        Places = [];
        Companies = [];
        Contacts = [];
        Statuses = [];
        JobTypes = [];

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && SelectedPlace is not null
            && SelectedCompany is not null && SelectedContact is not null
            && JobTypes.Any(j => j.IsSelected));
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => !IsBusy && _projectId > 0);
        OpenPlacePickerCommand = new AsyncRelayCommand(OpenPlacePickerAsync);
        OpenCompanyPickerCommand = new AsyncRelayCommand(OpenCompanyPickerAsync);
    }

    public event Action<bool>? RequestClose;

    public event Func<Task<PlaceDto?>>? RequestPlacePicker;

    public event Func<Task<(CompanyDto? Company, ContactDto? Contact)>>? RequestCompanyPicker;

    public ProjectSelectorViewModel ParentProjectSelector { get; }

    public ObservableCollection<PlaceDto> Places { get; }

    public ObservableCollection<CompanyDto> Companies { get; }

    public ObservableCollection<ContactDto> Contacts { get; }

    public ObservableCollection<ProjectFilterOptionDto> Statuses { get; }

    public ObservableCollection<ProjectJobTypeEditRowViewModel> JobTypes { get; }

    public string ProjectNumberDisplay
    {
        get => _projectNumberDisplay;
        private set => SetField(ref _projectNumberDisplay, value);
    }

    public string ProjectTitle
    {
        get => _projectTitle;
        private set => SetField(ref _projectTitle, value);
    }

    public string RenameTitle
    {
        get => _renameTitle;
        set => SetField(ref _renameTitle, value ?? string.Empty);
    }

    public PlaceDto? SelectedPlace
    {
        get => _selectedPlace;
        set
        {
            if (SetField(ref _selectedPlace, value))
                (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public CompanyDto? SelectedCompany
    {
        get => _selectedCompany;
        set
        {
            if (SetField(ref _selectedCompany, value))
            {
                _ = ReloadContactsAsync();
                (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ContactDto? SelectedContact
    {
        get => _selectedContact;
        set
        {
            if (SetField(ref _selectedContact, value))
                (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ProjectFilterOptionDto? SelectedStatus
    {
        get => _selectedStatus;
        set => SetField(ref _selectedStatus, value);
    }

    public string ApproveDescription
    {
        get => _approveDescription;
        set => SetField(ref _approveDescription, value ?? string.Empty);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetField(ref _validationMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (RenameCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool SavedSuccessfully { get; private set; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand OpenPlacePickerCommand { get; }

    public ICommand OpenCompanyPickerCommand { get; }

    public async Task InitializeAsync(int projectId, CancellationToken cancellationToken = default)
    {
        _projectId = projectId;
        IsBusy = true;
        ValidationMessage = string.Empty;
        try
        {
            var dto = await _updateService.GetForEditAsync(projectId, cancellationToken).ConfigureAwait(true);
            if (dto is null)
            {
                ValidationMessage = "הפרויקט לא נמצא.";
                return;
            }

            ProjectNumberDisplay = dto.ProjectNumberDisplay;
            ProjectTitle = dto.Title;
            RenameTitle = dto.Title;
            ApproveDescription = dto.ApproveDescription ?? string.Empty;

            Places.Clear();
            foreach (var place in await _places.ListAsync(cancellationToken).ConfigureAwait(true))
            {
                if (place.InUse || place.Id == dto.PlaceId)
                    Places.Add(place);
            }

            Companies.Clear();
            foreach (var company in await _companies.ListCompaniesAsync(cancellationToken).ConfigureAwait(true))
                Companies.Add(company);

            Statuses.Clear();
            var filters = await _filterOptions.GetFilterOptionsAsync(cancellationToken).ConfigureAwait(true);
            foreach (var status in filters.Statuses)
                Statuses.Add(status);

            var workers = await _users.GetActiveUsersAsync(cancellationToken).ConfigureAwait(true);
            JobTypes.Clear();
            foreach (var line in dto.JobTypes)
            {
                var row = new ProjectJobTypeEditRowViewModel(line, workers);
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ProjectJobTypeEditRowViewModel.IsSelected))
                        (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                };
                JobTypes.Add(row);
            }

            _loadedSelectedJobTypeIds = JobTypes
                .Where(j => j.IsSelected)
                .Select(j => j.JobTypeId)
                .ToHashSet();

            SelectedPlace = Places.FirstOrDefault(p => p.Id == dto.PlaceId);
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == dto.CompanyId);
            await ReloadContactsAsync(cancellationToken).ConfigureAwait(true);
            SelectedContact = Contacts.FirstOrDefault(c => c.Id == dto.ContactId);
            SelectedStatus = Statuses.FirstOrDefault(s => s.Id == dto.ProjectStatusId);

            await ParentProjectSelector.InitializeAsync().ConfigureAwait(true);
            if (dto.ParentProjectId is int parentId and > 0)
            {
                var parent = await _projectQuery
                    .GetProjectAsync(parentId, cancellationToken)
                    .ConfigureAwait(true);
                if (parent is not null)
                {
                    await _parentProjectContext
                        .SetCurrentProjectAsync(parent, cancellationToken)
                        .ConfigureAwait(true);
                }
            }

            (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (RenameCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ParentProjectSelector.Dispose();
    }

    private async Task SaveAsync()
    {
        ValidationMessage = string.Empty;
        IsBusy = true;
        try
        {
            var remainingIds = JobTypes
                .Where(j => j.IsSelected)
                .Select(j => j.JobTypeId)
                .ToHashSet();
            var removing = _loadedSelectedJobTypeIds.Where(id => !remainingIds.Contains(id)).ToList();
            if (removing.Count > 0)
            {
                var risks = await _updateService
                    .GetJobTypeRemovalRiskAsync(_projectId, remainingIds, CancellationToken.None)
                    .ConfigureAwait(true);
                if (risks.Count > 0)
                {
                    var lines = string.Join(
                        Environment.NewLine,
                        risks.Select(r =>
                            $"• #{r.WorkflowInstanceId} · {r.WorkflowName} · סוג {r.JobTypeTitle ?? r.JobTypeId.ToString()} · {r.StatusLabel}"));
                    var confirm = MessageBox.Show(
                        "הסרת סוג פרויקט היא פעולה משמעותית ולא מומלצת."
                        + Environment.NewLine
                        + "מופעי תהליך פתוחים לא יימחקו — הם יסומנו כמסלול יתום (סוג הוסר)."
                        + Environment.NewLine + Environment.NewLine
                        + lines
                        + Environment.NewLine + Environment.NewLine
                        + "להמשיך בשמירה?",
                        "אזהרה — הסרת סוג פרויקט",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (confirm != MessageBoxResult.Yes)
                    {
                        ValidationMessage = "השמירה בוטלה — סוגי פרויקט לא הוסרו.";
                        return;
                    }
                }
            }

            var result = await _updateService.SaveAsync(
                    new UpdateProjectCommand(
                        _projectId,
                        SelectedPlace!.Id,
                        SelectedCompany!.Id,
                        SelectedContact!.Id,
                        ParentProjectId: _parentProjectContext.CurrentProject?.ProjectId,
                        ProjectStatusId: SelectedStatus?.Id,
                        ApproveDescription,
                        JobTypes.Select(j => j.ToLine()).ToList()),
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                ValidationMessage = result.ErrorMessage ?? "שמירת הפרויקט נכשלה.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RenameAsync()
    {
        ValidationMessage = string.Empty;
        var desired = RenameTitle.Trim();
        if (string.IsNullOrWhiteSpace(desired))
        {
            ValidationMessage = "יש להזין שם חדש לשינוי שם.";
            return;
        }

        IsBusy = true;
        try
        {
            var analysis = await _renameOrchestrator
                .AnalyzeAsync(_projectId, desired, CancellationToken.None)
                .ConfigureAwait(true);
            if (!analysis.CanExecute)
            {
                MessageBox.Show(
                    analysis.ReasonIfCannot ?? "לא ניתן לשנות שם.",
                    "שינוי שם פרויקט",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var checklist = new StringBuilder();
            checklist.AppendLine($"שינוי שם: '{analysis.CurrentTitle}' → '{analysis.DesiredTitle}'");
            checklist.AppendLine($"NameAndNumber: '{analysis.CurrentNameAndNumber}' → '{analysis.PredictedNameAndNumber}'");
            checklist.AppendLine();
            foreach (var step in analysis.Steps)
                checklist.AppendLine($"• {step.Description}");
            checklist.AppendLine();
            checklist.Append("להמשיך?");

            var confirm = MessageBox.Show(
                checklist.ToString(),
                "אישור שינוי שם",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            var execute = await _renameOrchestrator
                .ExecuteAsync(analysis, CancellationToken.None)
                .ConfigureAwait(true);

            var report = new StringBuilder();
            foreach (var step in execute.Steps)
                report.AppendLine($"{step.Kind}: {step.Status} — {step.Message}");
            if (!execute.Succeeded)
                report.AppendLine().Append(execute.ErrorMessage);

            MessageBox.Show(
                report.ToString(),
                execute.Succeeded ? "שינוי שם הושלם" : "שינוי שם נכשל",
                MessageBoxButton.OK,
                execute.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Error);

            if (execute.Succeeded)
            {
                ProjectTitle = analysis.DesiredTitle;
                RenameTitle = analysis.DesiredTitle;
            }
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenPlacePickerAsync()
    {
        if (RequestPlacePicker is null)
            return;
        var selected = await RequestPlacePicker().ConfigureAwait(true);
        if (selected is null)
            return;
        if (Places.All(p => p.Id != selected.Id))
            Places.Add(selected);
        SelectedPlace = Places.First(p => p.Id == selected.Id);
    }

    private async Task OpenCompanyPickerAsync()
    {
        if (RequestCompanyPicker is null)
            return;
        var (company, contact) = await RequestCompanyPicker().ConfigureAwait(true);
        if (company is null)
            return;
        if (Companies.All(c => c.Id != company.Id))
            Companies.Add(company);
        SelectedCompany = Companies.First(c => c.Id == company.Id);
        await ReloadContactsAsync().ConfigureAwait(true);
        if (contact is not null)
        {
            if (Contacts.All(c => c.Id != contact.Id))
                Contacts.Add(contact);
            SelectedContact = Contacts.FirstOrDefault(c => c.Id == contact.Id);
        }
    }

    private async Task ReloadContactsAsync(CancellationToken cancellationToken = default)
    {
        Contacts.Clear();
        SelectedContact = null;
        if (SelectedCompany is null)
            return;
        foreach (var contact in await _companies.ListContactsAsync(SelectedCompany.Id, cancellationToken).ConfigureAwait(true))
            Contacts.Add(contact);
    }
}
