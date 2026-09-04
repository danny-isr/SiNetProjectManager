using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Shell;

/// <summary>View-model for the pending-approval restricted shell.</summary>
public sealed class PendingIdentityViewModel : INotifyPropertyChanged
{
    private readonly IIdentityCoherenceService _coherence;
    private readonly Action? _onBecameAuthorized;
    private string _statusMessage = string.Empty;

    public PendingIdentityViewModel(
        CurrentUserProfileDto profile,
        IIdentityCoherenceService coherence,
        Action? onBecameAuthorized = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _coherence = coherence ?? throw new ArgumentNullException(nameof(coherence));
        _onBecameAuthorized = onBecameAuthorized;

        var name = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.LoginName ?? $"#{profile.UserId}"
            : profile.DisplayName;
        UserLine = $"משתמש: {name}";

        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        ExitCommand = new RelayCommand(_ => System.Windows.Application.Current?.Shutdown());
    }

    public string UserLine { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand ExitCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task RefreshAsync()
    {
        try
        {
            StatusMessage = "מרענן…";
            var snapshot = await _coherence.RefreshSiUserAndEvaluateAsync().ConfigureAwait(true);
            if (snapshot.Status is IdentityCoherenceStatus.PendingApproval)
            {
                StatusMessage = "עדיין ממתין לאישור מנהל מערכת.";
                return;
            }

            if (snapshot.Status is IdentityCoherenceStatus.Blocked)
            {
                StatusMessage = "המשתמש חסום. פנה למנהל המערכת.";
                return;
            }

            StatusMessage = "ההרשאות עודכנו — טוען מצב מלא…";
            _onBecameAuthorized?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה ברענון: {ex.Message}";
        }
    }
}
