using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>Editable place row for the place picker grid.</summary>
public sealed class PlacePickerRowViewModel : ObservableObject
{
    private string _title;
    private string _cityIcon;
    private bool _inUse;
    private bool _isSaving;

    public PlacePickerRowViewModel(PlaceDto place)
    {
        ArgumentNullException.ThrowIfNull(place);
        Id = place.Id;
        _title = place.Title;
        _cityIcon = place.CityIcon ?? string.Empty;
        _inUse = place.InUse;
    }

    public int Id { get; private set; }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    /// <summary>Place number / city icon shown in the grid and used when creating projects.</summary>
    public string CityIcon
    {
        get => _cityIcon;
        set => SetField(ref _cityIcon, value ?? string.Empty);
    }

    public bool InUse
    {
        get => _inUse;
        set
        {
            if (SetField(ref _inUse, value))
            {
                InUseChanged?.Invoke(this);
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetField(ref _isSaving, value);
    }

    public event Action<PlacePickerRowViewModel>? InUseChanged;

    public PlaceDto ToDto() =>
        new(Id, Title.Trim(), string.IsNullOrWhiteSpace(CityIcon) ? null : CityIcon.Trim(), InUse);

    public void ApplySaved(PlaceDto saved)
    {
        Id = saved.Id;
        Title = saved.Title;
        CityIcon = saved.CityIcon ?? string.Empty;
        // Avoid re-entrant save when syncing from server.
        if (_inUse != saved.InUse)
        {
            _inUse = saved.InUse;
            OnPropertyChanged(nameof(InUse));
        }
    }
}

public sealed class PlacePickerDialogViewModel : ObservableObject
{
    private readonly IPlaceCatalogService _places;
    private string _filter = string.Empty;
    private PlacePickerRowViewModel? _selectedPlace;
    private string _newPlaceTitle = string.Empty;
    private string _newPlaceNumber = string.Empty;
    private bool _showInactive;
    private string _statusMessage = string.Empty;
    private bool _suppressInUseSave;

    public PlacePickerDialogViewModel(IPlaceCatalogService places)
    {
        _places = places ?? throw new ArgumentNullException(nameof(places));
        Places = [];
        SelectCommand = new RelayCommand(
            _ => RequestClose?.Invoke(true),
            _ => SelectedPlace is { InUse: true });
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        AddPlaceCommand = new AsyncRelayCommand(
            AddPlaceAsync,
            () => !string.IsNullOrWhiteSpace(NewPlaceTitle));
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<PlacePickerRowViewModel> Places { get; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>When false (default), only active places are listed.</summary>
    public bool ShowInactive
    {
        get => _showInactive;
        set
        {
            if (SetField(ref _showInactive, value))
            {
                ApplyFilter();
            }
        }
    }

    public PlacePickerRowViewModel? SelectedPlace
    {
        get => _selectedPlace;
        set
        {
            if (SetField(ref _selectedPlace, value))
            {
                (SelectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Selected place as DTO for the create-project form.</summary>
    public PlaceDto? SelectedPlaceDto => SelectedPlace?.ToDto();

    public string NewPlaceTitle
    {
        get => _newPlaceTitle;
        set
        {
            if (SetField(ref _newPlaceTitle, value))
            {
                (AddPlaceCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewPlaceNumber
    {
        get => _newPlaceNumber;
        set => SetField(ref _newPlaceNumber, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ICommand SelectCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand AddPlaceCommand { get; }

    private List<PlacePickerRowViewModel> _all = [];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var places = await _places.ListAsync(cancellationToken).ConfigureAwait(true);
        _all = places.Select(CreateRow).ToList();
        ApplyFilter();
    }

    private PlacePickerRowViewModel CreateRow(PlaceDto place)
    {
        var row = new PlacePickerRowViewModel(place);
        row.InUseChanged += OnRowInUseChanged;
        return row;
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedPlace?.Id;
        Places.Clear();
        var term = Filter.Trim();
        foreach (var place in _all)
        {
            if (!ShowInactive && !place.InUse)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(term)
                && !place.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !place.CityIcon.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !place.Id.ToString().Contains(term, StringComparison.Ordinal))
            {
                continue;
            }

            Places.Add(place);
        }

        SelectedPlace = selectedId is int id
            ? Places.FirstOrDefault(p => p.Id == id)
            : null;
    }

    private async void OnRowInUseChanged(PlacePickerRowViewModel row)
    {
        if (_suppressInUseSave || row.Id <= 0 || row.IsSaving)
        {
            return;
        }

        try
        {
            row.IsSaving = true;
            StatusMessage = string.Empty;
            var saved = await _places.SaveAsync(row.ToDto()).ConfigureAwait(true);
            _suppressInUseSave = true;
            try
            {
                row.ApplySaved(saved);
            }
            finally
            {
                _suppressInUseSave = false;
            }

            StatusMessage = saved.InUse ? "המקום סומן כפעיל." : "המקום סומן כלא פעיל.";
            ApplyFilter();
            (SelectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            // Revert UI toggle on failure.
            _suppressInUseSave = true;
            try
            {
                row.InUse = !row.InUse;
            }
            finally
            {
                _suppressInUseSave = false;
            }
        }
        finally
        {
            row.IsSaving = false;
        }
    }

    private async Task AddPlaceAsync()
    {
        try
        {
            var created = await _places.SaveAsync(new PlaceDto(
                    0,
                    NewPlaceTitle.Trim(),
                    string.IsNullOrWhiteSpace(NewPlaceNumber) ? null : NewPlaceNumber.Trim(),
                    InUse: true))
                .ConfigureAwait(true);
            var row = CreateRow(created);
            _all.Add(row);
            NewPlaceTitle = string.Empty;
            NewPlaceNumber = string.Empty;
            ApplyFilter();
            SelectedPlace = Places.FirstOrDefault(p => p.Id == created.Id);
            StatusMessage = "המקום נוסף.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}

public sealed class CompanyContactPickerDialogViewModel : ObservableObject
{
    private readonly ICompanyCatalogService _companies;
    private string _companyFilter = string.Empty;
    private string _contactFilter = string.Empty;
    private CompanyDto? _selectedCompany;
    private ContactDto? _selectedContact;
    private string _newCompanyTitle = string.Empty;
    private string _newContactName = string.Empty;
    private string _statusMessage = string.Empty;

    public CompanyContactPickerDialogViewModel(ICompanyCatalogService companies)
    {
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        Companies = [];
        Contacts = [];
        SelectCommand = new RelayCommand(
            _ => RequestClose?.Invoke(true),
            _ => SelectedCompany is not null && SelectedContact is not null);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        AddCompanyCommand = new AsyncRelayCommand(AddCompanyAsync, () => !string.IsNullOrWhiteSpace(NewCompanyTitle));
        AddContactCommand = new AsyncRelayCommand(
            AddContactAsync,
            () => SelectedCompany is not null && !string.IsNullOrWhiteSpace(NewContactName));
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<CompanyDto> Companies { get; }

    public ObservableCollection<ContactDto> Contacts { get; }

    public string CompanyFilter
    {
        get => _companyFilter;
        set
        {
            if (SetField(ref _companyFilter, value))
            {
                ApplyCompanyFilter();
            }
        }
    }

    public string ContactFilter
    {
        get => _contactFilter;
        set
        {
            if (SetField(ref _contactFilter, value))
            {
                ApplyContactFilter();
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
                (SelectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (AddContactCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
                (SelectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewCompanyTitle
    {
        get => _newCompanyTitle;
        set
        {
            if (SetField(ref _newCompanyTitle, value))
            {
                (AddCompanyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewContactName
    {
        get => _newContactName;
        set
        {
            if (SetField(ref _newContactName, value))
            {
                (AddContactCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ICommand SelectCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand AddCompanyCommand { get; }

    public ICommand AddContactCommand { get; }

    private List<CompanyDto> _allCompanies = [];
    private List<ContactDto> _allContacts = [];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _allCompanies = (await _companies.ListCompaniesAsync(cancellationToken).ConfigureAwait(true)).ToList();
        ApplyCompanyFilter();
    }

    private void ApplyCompanyFilter()
    {
        Companies.Clear();
        var term = CompanyFilter.Trim();
        foreach (var company in _allCompanies)
        {
            if (string.IsNullOrEmpty(term)
                || company.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                Companies.Add(company);
            }
        }
    }

    private void ApplyContactFilter()
    {
        Contacts.Clear();
        var term = ContactFilter.Trim();
        foreach (var contact in _allContacts)
        {
            if (string.IsNullOrEmpty(term)
                || contact.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                Contacts.Add(contact);
            }
        }
    }

    private async Task ReloadContactsAsync()
    {
        Contacts.Clear();
        SelectedContact = null;
        _allContacts = [];
        if (SelectedCompany is null)
        {
            return;
        }

        _allContacts = (await _companies.ListContactsAsync(SelectedCompany.Id).ConfigureAwait(true)).ToList();
        ApplyContactFilter();
    }

    private async Task AddCompanyAsync()
    {
        try
        {
            var created = await _companies.AddCompanyAsync(NewCompanyTitle.Trim()).ConfigureAwait(true);
            _allCompanies.Add(created);
            NewCompanyTitle = string.Empty;
            ApplyCompanyFilter();
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == created.Id);
            StatusMessage = "החברה נוספה.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task AddContactAsync()
    {
        if (SelectedCompany is null)
        {
            return;
        }

        try
        {
            var created = await _companies
                .AddContactAsync(SelectedCompany.Id, NewContactName.Trim())
                .ConfigureAwait(true);
            _allContacts.Add(created);
            NewContactName = string.Empty;
            ApplyContactFilter();
            SelectedContact = Contacts.FirstOrDefault(c => c.Id == created.Id);
            StatusMessage = "איש הקשר נוסף.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
