using SiNet.App.Wpf.Inspection;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>
/// Editable user row with dirty tracking for the native user-management screen.
/// </summary>
public sealed class UserEditRow : ObservableObject
{
    private string _displayName;
    private string _email;
    private string _loginName;
    private AppAccUserType _accUserType;
    private AppRole _role;
    private bool _isActive;
    private int? _masterPlanEmployeeId;
    private string? _masterPlanEmployeeName;
    private string _notes;

    public UserEditRow(UserSummaryDto source)
    {
        UserId = source.UserId;
        OpenTaskCount = source.OpenTaskCount;
        IsDomainGroup = source.IsDomainGroup;

        _displayName = source.DisplayName;
        _email = source.Email;
        _loginName = source.LoginName;
        _accUserType = source.AccUserType;
        _role = source.Role;
        _isActive = source.IsActive;
        _masterPlanEmployeeId = source.MasterPlanEmployeeId;
        _notes = source.Notes ?? string.Empty;

        SnapshotOriginal();
    }

    public int UserId { get; }

    public int OpenTaskCount { get; }

    public bool? IsDomainGroup { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
            {
                NotifyDirtyChanged();
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (SetField(ref _email, value))
            {
                NotifyDirtyChanged();
            }
        }
    }

    public string LoginName
    {
        get => _loginName;
        set
        {
            if (SetField(ref _loginName, value))
            {
                NotifyDirtyChanged();
            }
        }
    }

    public AppAccUserType AccUserType
    {
        get => _accUserType;
        set
        {
            if (SetField(ref _accUserType, value))
            {
                OnPropertyChanged(nameof(AccUserTypeDisplay));
                NotifyDirtyChanged();
            }
        }
    }

    public string AccUserTypeDisplay => AppAccUserTypeDisplay.GetDisplayName(AccUserType);

    public AppRole Role
    {
        get => _role;
        set
        {
            if (SetField(ref _role, value))
            {
                OnPropertyChanged(nameof(RoleDisplay));
                NotifyDirtyChanged();
            }
        }
    }

    public string RoleDisplay => AppRoleDisplay.GetDisplayName(Role);

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetField(ref _isActive, value))
            {
                NotifyDirtyChanged();
            }
        }
    }

    public int? MasterPlanEmployeeId
    {
        get => _masterPlanEmployeeId;
        set
        {
            if (SetField(ref _masterPlanEmployeeId, value))
            {
                NotifyDirtyChanged();
            }
        }
    }

    public string? MasterPlanEmployeeName
    {
        get => _masterPlanEmployeeName;
        set => SetField(ref _masterPlanEmployeeName, value);
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (SetField(ref _notes, value))
            {
                NotifyDirtyChanged();
            }
        }
    }

    public bool IsDirty { get; private set; }

    private string _originalDisplayName = string.Empty;
    private string _originalEmail = string.Empty;
    private string _originalLoginName = string.Empty;
    private AppAccUserType _originalAccUserType;
    private AppRole _originalRole;
    private bool _originalIsActive;
    private int? _originalMasterPlanEmployeeId;
    private string _originalNotes = string.Empty;

    public void RevertChanges()
    {
        DisplayName = _originalDisplayName;
        Email = _originalEmail;
        LoginName = _originalLoginName;
        AccUserType = _originalAccUserType;
        Role = _originalRole;
        IsActive = _originalIsActive;
        MasterPlanEmployeeId = _originalMasterPlanEmployeeId;
        Notes = _originalNotes;
        SnapshotOriginal();
    }

    public UpdateUserCommand ToUpdateCommand()
        => new(
            UserId,
            DisplayName,
            Email,
            LoginName,
            AccUserType,
            Role,
            IsActive,
            MasterPlanEmployeeId,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());

    private void SnapshotOriginal()
    {
        _originalDisplayName = _displayName;
        _originalEmail = _email;
        _originalLoginName = _loginName;
        _originalAccUserType = _accUserType;
        _originalRole = _role;
        _originalIsActive = _isActive;
        _originalMasterPlanEmployeeId = _masterPlanEmployeeId;
        _originalNotes = _notes;
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void NotifyDirtyChanged()
    {
        var dirty = _displayName != _originalDisplayName
                    || _email != _originalEmail
                    || _loginName != _originalLoginName
                    || _accUserType != _originalAccUserType
                    || _role != _originalRole
                    || _isActive != _originalIsActive
                    || _masterPlanEmployeeId != _originalMasterPlanEmployeeId
                    || _notes != _originalNotes;

        if (IsDirty != dirty)
        {
            IsDirty = dirty;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public void MarkClean() => SnapshotOriginal();
}
