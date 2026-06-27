using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SiNet.Application.Abstractions.Email;
using SiNet.Infrastructure.Google;

namespace SiNet.App.Wpf.Inbox;

/// <summary>
/// Minimal vertical-slice ViewModel that proves the new stack end-to-end: it resolves the native
/// <see cref="IEmailGateway"/> port (served by <c>GmailEmailGateway</c> over the Gmail API) and
/// lists a real per-project inbox. It also exposes an explicit "Connect Google" action that drives
/// the native <see cref="GmailClientProvider"/> interactive sign-in. UI concerns (status text,
/// busy flag) live here in the WPF layer, never in the connector.
/// </summary>
public sealed class InboxViewModel : INotifyPropertyChanged
{
    private readonly IEmailGateway _emailGateway;
    private readonly GmailClientProvider _gmail;

    private string _location = string.Empty;
    private string _projectName = string.Empty;
    private string _status = "Enter a location and project, then Load.";
    private bool _isBusy;

    public InboxViewModel(IEmailGateway emailGateway, GmailClientProvider gmail)
    {
        _emailGateway = emailGateway;
        _gmail = gmail;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
    }

    public ObservableCollection<EmailRow> Emails { get; } = [];

    public ICommand LoadCommand { get; }

    public ICommand ConnectCommand { get; }

    /// <summary>Reflects whether a Gmail session is currently established.</summary>
    public bool IsConnected => _gmail.IsSignedIn;

    public string Location
    {
        get => _location;
        set => SetField(ref _location, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetField(ref _projectName, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                (LoadCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ConnectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task ConnectAsync()
    {
        IsBusy = true;
        Status = "Connecting to Google… a browser may open for sign-in.";

        try
        {
            var result = await _gmail.SignInInteractiveAsync().ConfigureAwait(true);
            Status = result switch
            {
                GmailSignInResult.Success => "Connected to Google. You can now load a project inbox.",
                GmailSignInResult.NotConfigured =>
                    "Google client secrets are not configured. Set the 'Gmail:ClientSecretsPath' setting.",
                _ => "Google sign-in did not complete. Please try again.",
            };
        }
        catch (Exception ex)
        {
            Status = $"Google sign-in failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(Location) || string.IsNullOrWhiteSpace(ProjectName))
        {
            Status = "Location and project name are required.";
            return;
        }

        IsBusy = true;
        Status = "Loading…";
        Emails.Clear();

        try
        {
            var emails = await _emailGateway
                .GetProjectEmailsAsync(Location.Trim(), ProjectName.Trim())
                .ConfigureAwait(true);

            foreach (var summary in emails)
            {
                Emails.Add(EmailRow.FromSummary(summary));
            }

            Status = Emails.Count == 0
                ? "No emails found for this project (or not signed in to Gmail)."
                : $"Loaded {Emails.Count} email(s).";
        }
        catch (Exception ex)
        {
            Status = $"Failed to load emails: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>Display projection of an <see cref="EmailSummary"/> for the inbox list.</summary>
public sealed record EmailRow(string From, string Subject, string Received, bool HasAttachments)
{
    public static EmailRow FromSummary(EmailSummary summary) => new(
        summary.From.Value,
        summary.Subject,
        summary.ReceivedAt == DateTimeOffset.MinValue
            ? string.Empty
            : summary.ReceivedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
        summary.HasAttachments);
}
