using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// View model for the clean New System shell (see <c>docs/APP_SHELL.md</c> §1/§6). It is deliberately
/// thin and WPF-window-free so it can be unit-tested without opening a window:
/// <list type="bullet">
/// <item><description>It exposes header/status text, window title, and the current user/project display strings.</description></item>
/// <item><description>It holds a <b>migrated-only</b> menu (<see cref="MenuItems"/>) supplied by the
/// host, so the shell never scans or copies the legacy menu (see <c>docs/APP_SHELL.md</c> §5/§6).</description></item>
/// <item><description>It carries <b>no business logic</b> and never mutates workflow (see
/// <c>AI_DEVELOPMENT_GUIDE.md</c> rule 11): each menu item only opens a surface via DI/a factory.</description></item>
/// </list>
/// Observes the shared <see cref="ICurrentProjectContext"/> for <see cref="WindowTitle"/> and
/// <see cref="CurrentProjectDisplay"/>.
/// </summary>
public class NewShellViewModel : INotifyPropertyChanged, IDisposable
{
    private const string NoProjectText = "לא נבחר פרויקט";
    private const string DefaultStatusText = "מוכן";

    private readonly ICurrentProjectContext? _currentProjectContext;

    private string _currentUserDisplay;
    private string _currentProjectDisplay;
    private string _windowTitle;
    private string _statusText;

    /// <summary>
    /// Creates the shell view model.
    /// </summary>
    /// <param name="menuItems">
    /// The migrated-only menu items to show. Built by the host from DI/factories; the shell does not
    /// discover them. May be empty but not <see langword="null"/>.
    /// </param>
    /// <param name="currentUserDisplay">Friendly current-user text (name), or null/empty if unknown.</param>
    /// <param name="currentProjectContext">
    /// Shared project context; when supplied, <see cref="WindowTitle"/> and
    /// <see cref="CurrentProjectDisplay"/> track <see cref="ICurrentProjectContext.CurrentProject"/>.
    /// </param>
    /// <param name="currentProjectDisplay">
    /// Initial current-project header text when no context is supplied (design-time only).
    /// </param>
    /// <param name="openNewProject">
    /// Optional host action that opens the native New Project dialog. When null, the header button is hidden.
    /// </param>
    public NewShellViewModel(
        IEnumerable<NewShellMenuItem> menuItems,
        string? currentUserDisplay,
        ICurrentProjectContext? currentProjectContext = null,
        string? currentProjectDisplay = null,
        Action? openNewProject = null)
    {
        ArgumentNullException.ThrowIfNull(menuItems);

        MenuItems = new ObservableCollection<NewShellMenuItem>(menuItems);
        _currentUserDisplay = string.IsNullOrWhiteSpace(currentUserDisplay)
            ? "משתמש לא ידוע"
            : currentUserDisplay;
        _currentProjectContext = currentProjectContext;
        _windowTitle = NewShellWindowTitle.Format(_currentProjectContext?.CurrentProject);
        _currentProjectDisplay = string.IsNullOrWhiteSpace(currentProjectDisplay)
            ? NoProjectText
            : currentProjectDisplay!;
        _statusText = DefaultStatusText;

        CanOpenNewProject = openNewProject is not null;
        OpenNewProjectCommand = new RelayCommand(_ => openNewProject?.Invoke(), _ => CanOpenNewProject);

        if (_currentProjectContext is not null)
        {
            ApplyProject(_currentProjectContext.CurrentProject);
            _currentProjectContext.CurrentProjectChanged += OnCurrentProjectChanged;
        }
    }

    /// <summary>Header branding inside the shell chrome (not the OS window title).</summary>
    public string Title => "SiNet — מערכת חדשה";

    /// <summary>OS window title; reflects the shared current project when selected.</summary>
    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetField(ref _windowTitle, value);
    }

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

    /// <summary>Current-project text shown in the header bar.</summary>
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

    /// <summary>True when the header New Project button should be shown.</summary>
    public bool CanOpenNewProject { get; }

    /// <summary>Opens the native New Project dialog when available.</summary>
    public ICommand OpenNewProjectCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_currentProjectContext is not null)
        {
            _currentProjectContext.CurrentProjectChanged -= OnCurrentProjectChanged;
        }
    }

    internal void ApplyProject(ProjectSummaryDto? project)
    {
        WindowTitle = NewShellWindowTitle.Format(project);
        var header = NewShellWindowTitle.FormatHeaderDisplay(project);
        CurrentProjectDisplay = string.IsNullOrWhiteSpace(header) ? NoProjectText : header;
    }

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyProject(e.Project), DispatcherPriority.Background);
            return;
        }

        ApplyProject(e.Project);
    }

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
