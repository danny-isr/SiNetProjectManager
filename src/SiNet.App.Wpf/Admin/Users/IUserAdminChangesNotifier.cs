namespace SiNet.App.Wpf.Admin.Users;

/// <summary>
/// Lightweight in-process signal so native admin surfaces can refresh after user mutations.
/// </summary>
public interface IUserAdminChangesNotifier
{
    event EventHandler? UsersChanged;

    void NotifyUsersChanged();
}

/// <inheritdoc />
public sealed class UserAdminChangesNotifier : IUserAdminChangesNotifier
{
    public event EventHandler? UsersChanged;

    public void NotifyUsersChanged() => UsersChanged?.Invoke(this, EventArgs.Empty);
}
