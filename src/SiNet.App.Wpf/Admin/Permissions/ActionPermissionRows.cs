using SiNet.App.Wpf.Inspection;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Permissions;

/// <summary>Action row in the native action-permissions admin screen.</summary>
public sealed class ActionPermissionActionRow : ObservableObject
{
    public ActionPermissionActionRow(string actionCode, string displayName, int authorizedCount)
    {
        ActionCode = actionCode;
        DisplayName = displayName;
        _authorizedCount = authorizedCount;
    }

    public string ActionCode { get; }

    public string DisplayName { get; }

    private int _authorizedCount;

    public int AuthorizedCount
    {
        get => _authorizedCount;
        set => SetField(ref _authorizedCount, value);
    }
}

/// <summary>User checklist row for one action.</summary>
public sealed class ActionPermissionUserRow : ObservableObject
{
    public ActionPermissionUserRow(int userId, string displayName, string? email, bool isAuthorized)
    {
        UserId = userId;
        DisplayName = displayName;
        Email = email;
        _isAuthorized = isAuthorized;
    }

    public int UserId { get; }

    public string DisplayName { get; }

    public string? Email { get; }

    private bool _isAuthorized;

    public bool IsAuthorized
    {
        get => _isAuthorized;
        set => SetField(ref _isAuthorized, value);
    }
}
