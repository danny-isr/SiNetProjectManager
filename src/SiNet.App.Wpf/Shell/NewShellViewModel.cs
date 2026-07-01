using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// View model for the clean New System shell (see <c>docs/APP_SHELL.md</c> §1/§6). It is deliberately
/// thin and WPF-window-free so it can be unit-tested without opening a window:
/// <list type="bullet">
/// <item><description>It exposes header/status text and the current user/project display strings.</description></item>
/// <item><description>It holds a <b>migrated-only</b> menu (<see cref="MenuItems"/>) supplied by the
/// host, so the shell never scans or copies the legacy menu (see <c>docs/APP_SHELL.md</c> §5/§6).</description></item>
/// <item><description>It carries <b>no business logic</b> and never mutates workflow (see
/// <c>AI_DEVELOPMENT_GUIDE.md</c> rule 11): each menu item only opens a surface via DI/a factory.</description></item>
/// </list>
/// The current-project display can be updated by the host (which observes the shared
/// <c>ICurrentProjectContext</c>) via <see cref="SetCurrentProjectDisplay(string?)"/>.
/// </summary>
public class NewShellViewModel : INotifyPropertyChanged
{
    private const string NoProjectText = "לא נבחר פרויקט";
    private const string DefaultStatusText = "מוכן";

    private string _currentUserDisplay;
    private string _currentProjectDisplay;
    private string _statusText;

    /// <summary>
    /// Creates the shell view model.
    /// </summary>
    /// <param name="menuItems">
    /// The migrated-only menu items to show. Built by the host from DI/factories; the shell does not
    /// discover them. May be empty but not <see langword="null"/>.
    /// </param>
    /// <param name="currentUserDisplay">Friendly current-user text (name), or null/empty if unknown.</param>
    /// <param name="currentProjectDisplay">Initial current-project text, or null when none is selected.</param>
    public NewShellViewModel(
        IEnumerable<NewShellMenuItem> menuItems,
        string? currentUserDisplay,
        string? currentProjectDisplay = null)
    {
        ArgumentNullException.ThrowIfNull(menuItems);

        MenuItems = new ObservableCollection<NewShellMenuItem>(menuItems);
        _currentUserDisplay = string.IsNullOrWhiteSpace(currentUserDisplay)
            ? "משתמש לא ידוע"
            : currentUserDisplay;
        _currentProjectDisplay = string.IsNullOrWhiteSpace(currentProjectDisplay)
            ? NoProjectText
            : currentProjectDisplay!;
        _statusText = DefaultStatusText;
    }

    /// <summary>Window/header title for the clean shell.</summary>
    public string Title => "SiNet — מערכת חדשה";

    /// <summary>Header sub-label describing this is the isolated new-system shell.</summary>
    public string HeaderSubtitle => "מעטפת נקייה (ללא המערכת הישנה)";

    /// <summary>The migrated-only menu shown by the shell. Never the legacy menu.</summary>
    public ObservableCollection<NewShellMenuItem> MenuItems { get; }

    /// <summary>Friendly current-user text shown in the header.</summary>
    public string CurrentUserDisplay
    {
        get => _currentUserDisplay;
        private set => SetField(ref _currentUserDisplay, value);
    }

    /// <summary>Current-project text shown in the header; updated by the host on project change.</summary>
    public string CurrentProjectDisplay
    {
        get => _currentProjectDisplay;
        private set => SetField(ref _currentProjectDisplay, value);
    }

    /// <summary>Status-bar text.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    /// <summary>
    /// Updates the current-project display (called by the host when the shared
    /// <c>ICurrentProjectContext</c> changes). Empty/null shows the "no project" placeholder.
    /// </summary>
    public void SetCurrentProjectDisplay(string? projectDisplay) =>
        CurrentProjectDisplay = string.IsNullOrWhiteSpace(projectDisplay) ? NoProjectText : projectDisplay!;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
