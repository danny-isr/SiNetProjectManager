using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

public sealed class JobTypeOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private UserLookupDto? _adminWorker;
    private string _bidValueText = "0";

    public JobTypeOptionViewModel(
        JobTypeDto jobType,
        bool isSelected = false,
        IReadOnlyList<UserLookupDto>? workers = null)
    {
        JobType = jobType ?? throw new ArgumentNullException(nameof(jobType));
        _isSelected = isSelected;
        Workers = workers ?? [];
    }

    public JobTypeDto JobType { get; }

    public int Id => JobType.Id;

    public string Title => JobType.Title;

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

    public CreateProjectJobTypeLine ToLine()
    {
        if (!decimal.TryParse(BidValueText, NumberStyles.Number, CultureInfo.CurrentCulture, out var bid)
            && !decimal.TryParse(BidValueText, NumberStyles.Number, CultureInfo.InvariantCulture, out bid))
        {
            bid = 0m;
        }

        return new CreateProjectJobTypeLine(Id, AdminWorker?.UserId, bid);
    }
}

/// <summary>View model for the native New Project dialog.</summary>
public sealed class ProjectCreateDialogViewModel : ObservableObject, IDisposable
{
    private readonly IProjectCreateService _createService;
    private readonly IPlaceCatalogService _places;
    private readonly ICompanyCatalogService _companies;
    private readonly IJobTypeQueryService _jobTypes;
    private readonly IUserLookupService? _users;
    private readonly InMemoryCurrentProjectContext _parentProjectContext = new();

    private string _projectNumberDisplay = "—";
    private string _projectName = string.Empty;
    private PlaceDto? _selectedPlace;
    private CompanyDto? _selectedCompany;
    private ContactDto? _selectedContact;
    private string _approveDescription = string.Empty;
    private string _validationMessage = string.Empty;
    private bool _isSaving;
    private bool _disposed;

    public ProjectCreateDialogViewModel(
        IProjectCreateService createService,
        IPlaceCatalogService places,
        ICompanyCatalogService companies,
        IJobTypeQueryService jobTypes,
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService projectFilterOptions,
        IUserLookupService? users = null)
    {
        _createService = createService ?? throw new ArgumentNullException(nameof(createService));
        _places = places ?? throw new ArgumentNullException(nameof(places));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _jobTypes = jobTypes ?? throw new ArgumentNullException(nameof(jobTypes));
        _users = users;

        ParentProjectSelector = new ProjectSelectorViewModel(projectQuery, projectFilterOptions, _parentProjectContext);
        Places = [];
        Companies = [];
        Contacts = [];
        JobTypes = [];

        CreateCommand = new AsyncRelayCommand(CreateAsync, CanCreate);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
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

    public ObservableCollection<JobTypeOptionViewModel> JobTypes { get; }

    public string ProjectNumberDisplay
    {
        get => _projectNumberDisplay;
        private set => SetField(ref _projectNumberDisplay, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (SetField(ref _projectName, value))
            {
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public PlaceDto? SelectedPlace
    {
        get => _selectedPlace;
        set
        {
            if (SetField(ref _selectedPlace, value))
            {
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
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
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ContactDto? SelectedContact
    {
        get => _selectedContact;
        set
        {
            if (SetField(ref _selectedContact, value))
            {
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
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

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetField(ref _isSaving, value))
            {
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// When set, <see cref="CreateProjectCommand.EmailMessageId"/> links the new project to that inbox row.
    /// </summary>
    public int? EmailMessageId { get; set; }

    /// <summary>Label for the primary create button (default: אישור).</summary>
    public string PrimaryActionLabel { get; set; } = "אישור";

    public int? CreatedProjectId { get; private set; }

    public string? CreatedProjectTitle { get; private set; }

    public string? CreatedPlaceTitle { get; private set; }

    /// <summary>Non-fatal warning from create (e.g. ACC mapping provision failed).</summary>
    public string? CreatedWarningMessage { get; private set; }

    public ICommand CreateCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand OpenPlacePickerCommand { get; }

    public ICommand OpenCompanyPickerCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var nextNumber = await _createService.GetNextProjectNumberAsync(cancellationToken).ConfigureAwait(true);
        ProjectNumberDisplay = nextNumber.ToString("0");

        Places.Clear();
        foreach (var place in await _places.ListAsync(cancellationToken).ConfigureAwait(true))
        {
            if (place.InUse)
            {
                Places.Add(place);
            }
        }

        Companies.Clear();
        foreach (var company in await _companies.ListCompaniesAsync(cancellationToken).ConfigureAwait(true))
        {
            Companies.Add(company);
        }

        var workers = _users is null
            ? (IReadOnlyList<UserLookupDto>)[]
            : await _users.GetActiveUsersAsync(cancellationToken).ConfigureAwait(true);
        var defaultJobTypeId = await _jobTypes.ResolveDefaultJobTypeIdAsync(cancellationToken).ConfigureAwait(true);
        JobTypes.Clear();
        foreach (var jobType in await _jobTypes.ListAsync(cancellationToken).ConfigureAwait(true))
        {
            var option = new JobTypeOptionViewModel(jobType, isSelected: jobType.Id == defaultJobTypeId, workers);
            option.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(JobTypeOptionViewModel.IsSelected))
                {
                    (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            };
            JobTypes.Add(option);
        }

        await ParentProjectSelector.InitializeAsync().ConfigureAwait(true);
        (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ParentProjectSelector.Dispose();
    }

    private bool CanCreate() =>
        !IsSaving
        && !string.IsNullOrWhiteSpace(ProjectName)
        && SelectedPlace is not null
        && SelectedCompany is not null
        && SelectedContact is not null
        && JobTypes.Any(j => j.IsSelected);

    private async Task CreateAsync()
    {
        ValidationMessage = string.Empty;
        IsSaving = true;
        try
        {
            var name = ProjectName.Trim();
            if (name.Length > MaxProjectTitleLength)
            {
                ValidationMessage = $"שם הפרויקט לא יכול לעלות על {MaxProjectTitleLength} תווים.";
                return;
            }

            if (await _createService.ProjectNameExistsAsync(name).ConfigureAwait(true))
            {
                ValidationMessage = "שם הפרויקט כבר קיים במערכת.";
                return;
            }

            var selectedLines = JobTypes.Where(j => j.IsSelected).Select(j => j.ToLine()).ToList();
            var result = await _createService.CreateAsync(new CreateProjectCommand(
                    name,
                    SelectedPlace!.Id,
                    SelectedCompany!.Id,
                    SelectedContact!.Id,
                    selectedLines.Select(l => l.JobTypeId).ToList(),
                    ParentProjectId: _parentProjectContext.CurrentProject?.ProjectId,
                    EmailMessageId: EmailMessageId,
                    ApproveDescription: string.IsNullOrWhiteSpace(ApproveDescription) ? null : ApproveDescription.Trim(),
                    JobTypeLines: selectedLines))
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                ValidationMessage = result.ErrorMessage ?? "יצירת הפרויקט נכשלה.";
                return;
            }

            CreatedProjectId = result.ProjectId;
            CreatedProjectTitle = result.ProjectTitle;
            CreatedPlaceTitle = result.PlaceTitle;
            CreatedWarningMessage = result.WarningMessage;
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public const int MaxProjectTitleLength = 24;

    private async Task OpenPlacePickerAsync()
    {
        if (RequestPlacePicker is null)
        {
            return;
        }

        var selected = await RequestPlacePicker().ConfigureAwait(true);
        if (selected is null)
        {
            return;
        }

        var existing = Places.FirstOrDefault(p => p.Id == selected.Id);
        if (existing is null)
        {
            Places.Add(selected);
        }
        else
        {
            var index = Places.IndexOf(existing);
            Places[index] = selected;
        }

        SelectedPlace = Places.First(p => p.Id == selected.Id);
    }

    private async Task OpenCompanyPickerAsync()
    {
        if (RequestCompanyPicker is null)
        {
            return;
        }

        var (company, contact) = await RequestCompanyPicker().ConfigureAwait(true);
        if (company is null)
        {
            return;
        }

        if (Companies.All(c => c.Id != company.Id))
        {
            Companies.Add(company);
        }

        SelectedCompany = Companies.First(c => c.Id == company.Id);
        await ReloadContactsAsync().ConfigureAwait(true);
        if (contact is not null)
        {
            if (Contacts.All(c => c.Id != contact.Id))
            {
                Contacts.Add(contact);
            }

            SelectedContact = Contacts.FirstOrDefault(c => c.Id == contact.Id);
        }
    }

    private async Task ReloadContactsAsync()
    {
        Contacts.Clear();
        SelectedContact = null;
        if (SelectedCompany is null)
        {
            return;
        }

        foreach (var contact in await _companies.ListContactsAsync(SelectedCompany.Id).ConfigureAwait(true))
        {
            Contacts.Add(contact);
        }
    }
}
