using SiNet.App.Wpf.Inspection;
using SiNet.Application.MasterPlan;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public sealed class CompanyMappingRow : ObservableObject
{
    private int? _masterPlanCompanyId;
    private bool _isActive;
    private string? _matchStatus;
    private bool _isAutoMatch;
    private MpCompanyOptionDto? _selectedOption;

    public int SiNetId { get; init; }
    public string SiNetTitle { get; init; } = string.Empty;
    public string? SiNetEmail { get; init; }
    public string? SiNetPhone { get; init; }
    public int ProjectCount { get; init; }
    public int ContactCount { get; init; }
    public string RelationInfo => $"פרויקטים: {ProjectCount} | אנשי קשר: {ContactCount}";

    public IReadOnlyList<MpCompanyOptionDto> MpOptions { get; set; } = [];

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public int? MasterPlanCompanyId
    {
        get => _masterPlanCompanyId;
        set
        {
            if (SetField(ref _masterPlanCompanyId, value))
            {
                OnPropertyChanged(nameof(SelectedOption));
            }
        }
    }

    public string? MatchStatus
    {
        get => _matchStatus;
        set => SetField(ref _matchStatus, value);
    }

    public bool IsAutoMatch
    {
        get => _isAutoMatch;
        set => SetField(ref _isAutoMatch, value);
    }

    public MpCompanyOptionDto? SelectedOption
    {
        get => _selectedOption ?? MpOptions.FirstOrDefault(o => o.Id == MasterPlanCompanyId);
        set
        {
            _selectedOption = value;
            MasterPlanCompanyId = value?.Id;
            if (value is not null && string.IsNullOrWhiteSpace(MatchStatus))
            {
                MatchStatus = "ידני";
            }

            OnPropertyChanged();
        }
    }

    public static CompanyMappingRow FromDto(
        MasterPlanCompanyMappingDto dto,
        IReadOnlyList<MpCompanyOptionDto> options) =>
        new()
        {
            SiNetId = dto.SiNetId,
            SiNetTitle = dto.SiNetTitle,
            SiNetEmail = dto.SiNetEmail,
            SiNetPhone = dto.SiNetPhone,
            ProjectCount = dto.ProjectCount,
            ContactCount = dto.ContactCount,
            IsActive = dto.IsActive,
            MasterPlanCompanyId = dto.MasterPlanCompanyId,
            MatchStatus = dto.MatchStatus,
            IsAutoMatch = dto.IsAutoMatch,
            MpOptions = options,
        };

    public MasterPlanCompanyMappingChange ToChange() =>
        new(SiNetId, MasterPlanCompanyId, IsActive);
}

public sealed class ContactMappingRow : ObservableObject
{
    private int? _masterPlanContactId;
    private bool _isActive;
    private string? _matchStatus;
    private bool _isAutoMatch;
    private MpContactOptionDto? _selectedOption;

    public int SiNetId { get; init; }
    public string SiNetFullName { get; init; } = string.Empty;
    public int? SiNetCompanyId { get; init; }
    public string? SiNetCompanyTitle { get; init; }
    public string? SiNetEmail { get; init; }
    public string? SiNetPhone { get; init; }
    public int ProjectCount { get; init; }
    public string RelationInfo => $"פרויקטים: {ProjectCount}";

    public IReadOnlyList<MpContactOptionDto> MpOptions { get; set; } = [];

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public int? MasterPlanContactId
    {
        get => _masterPlanContactId;
        set
        {
            if (SetField(ref _masterPlanContactId, value))
            {
                OnPropertyChanged(nameof(SelectedOption));
            }
        }
    }

    public string? MatchStatus
    {
        get => _matchStatus;
        set => SetField(ref _matchStatus, value);
    }

    public bool IsAutoMatch
    {
        get => _isAutoMatch;
        set => SetField(ref _isAutoMatch, value);
    }

    public MpContactOptionDto? SelectedOption
    {
        get => _selectedOption ?? MpOptions.FirstOrDefault(o => o.Id == MasterPlanContactId);
        set
        {
            _selectedOption = value;
            MasterPlanContactId = value?.Id;
            if (value is not null && string.IsNullOrWhiteSpace(MatchStatus))
            {
                MatchStatus = "ידני";
            }

            OnPropertyChanged();
        }
    }

    public static ContactMappingRow FromDto(
        MasterPlanContactMappingDto dto,
        IReadOnlyList<MpContactOptionDto> options) =>
        new()
        {
            SiNetId = dto.SiNetId,
            SiNetFullName = dto.SiNetFullName,
            SiNetCompanyId = dto.SiNetCompanyId,
            SiNetCompanyTitle = dto.SiNetCompanyTitle,
            SiNetEmail = dto.SiNetEmail,
            SiNetPhone = dto.SiNetPhone,
            ProjectCount = dto.ProjectCount,
            IsActive = dto.IsActive,
            MasterPlanContactId = dto.MasterPlanContactId,
            MatchStatus = dto.MatchStatus,
            IsAutoMatch = dto.IsAutoMatch,
            MpOptions = options,
        };

    public MasterPlanContactMappingChange ToChange() =>
        new(SiNetId, MasterPlanContactId, IsActive);
}
