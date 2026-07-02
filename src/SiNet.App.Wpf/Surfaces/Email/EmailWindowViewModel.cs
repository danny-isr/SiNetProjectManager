using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// View model for <see cref="EmailWindowView"/> — the visual clone of the legacy
/// <c>EmailManagementView</c> (email management window).
/// <para>
/// <b>Visual-clone slice only.</b> This view model is intentionally thin and UI-facing: it exposes the
/// same general bindable surface (title, search text, selected folder/status, email list, selected
/// email + body, attachments, status bar) and the same command names as the old screen so the window
/// looks and feels familiar, but it carries <b>no</b> heavy legacy logic. It does NOT touch the
/// database, load real email, call Gmail/Outlook, access the file system, link projects, create tasks,
/// or mutate workflow. Every command is stubbed: it simply reports "not wired yet" via
/// <see cref="StatusMessage"/>. Data is fake/design-time only (<see cref="EmailWindowDesignData"/>).
/// </para>
/// <para>
/// Workflow-first direction is preserved structurally: the window can later be opened from a
/// Workflow/Task with a <see cref="WorkSurfaceContext"/> (see <see cref="ApplyContext"/>), after which
/// individual actions will be reconnected one at a time through clean Application services. This slice
/// does not implement task opening or task completion behavior.
/// </para>
/// <para>
/// Project selection is <b>not owned here</b>: the window hosts the shared
/// <see cref="ProjectSelectorViewModel"/> (bound in XAML via <see cref="ProjectSelector"/>) and only
/// <i>observes</i> the shared <see cref="ICurrentProjectContext"/> so <see cref="ActiveProjectDisplay"/>
/// reflects the Current Project (see <c>docs/PROJECTS.md</c> §5/§9). Both the selector and this view
/// model share one in-memory context in this slice; no real project source or email filtering is wired.
/// </para>
/// <para>
/// This is the visual-clone target. The old <c>EmailManagementView</c> remains the visual reference /
/// legacy source and is not modified.
/// </para>
/// </summary>
public sealed class EmailWindowViewModel : ObservableObject, IDisposable
{
    private const string NotWiredYet =
        "\u05E4\u05E2\u05D5\u05DC\u05D4 \u05D6\u05D5 \u05D8\u05E8\u05DD \u05D7\u05D5\u05D1\u05E8\u05D4 (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u05D1\u05DC\u05D1\u05D3)"; // "This action is not wired yet (visual shell only)"

    private readonly ICurrentProjectContext _currentProject;

    private string _searchText = string.Empty;
    private EmailFolderRow? _selectedFolder;
    private string? _selectedStatus;
    private EmailListRow? _selectedEmail;
    private bool _isBusy;
    private string _activeProjectDisplay =
        "\u05DC\u05D0 \u05E0\u05D1\u05D7\u05E8 \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8"; // "No project selected"
    private string _statusMessage =
        "\u05DE\u05D5\u05DB\u05DF (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u2014 \u05DC\u05DC\u05D0 \u05D7\u05D9\u05D1\u05D5\u05E8 \u05E0\u05EA\u05D5\u05E0\u05D9\u05DD)"; // "Ready (visual shell — no data connected)"

    public EmailWindowViewModel()
        : this(new FakeProjectQueryService(), new InMemoryCurrentProjectContext())
    {
    }

    /// <summary>
    /// Primary constructor: hosts the shared <see cref="ProjectSelectorViewModel"/> over the supplied
    /// read port and shared current-project context, and observes that context for display updates.
    /// </summary>
    public EmailWindowViewModel(IProjectQueryService projectQuery, ICurrentProjectContext currentProject)
    {
        ArgumentNullException.ThrowIfNull(projectQuery);
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));

        Folders = new ObservableCollection<EmailFolderRow>(EmailWindowDesignData.SampleFolders);
        StatusOptions = new ObservableCollection<string>(EmailWindowDesignData.SampleStatuses);
        Emails = new ObservableCollection<EmailListRow>(EmailWindowDesignData.SampleEmails);
        Attachments = new ObservableCollection<EmailAttachmentRow>(EmailWindowDesignData.SampleAttachments);

        _selectedFolder = Folders.FirstOrDefault();
        _selectedStatus = StatusOptions.FirstOrDefault();
        _selectedEmail = Emails.FirstOrDefault();

        ProjectSelector = new ProjectSelectorViewModel(projectQuery, _currentProject);
        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
        UpdateActiveProjectDisplay(_currentProject.CurrentProject);
        _ = ProjectSelector.InitializeAsync();

        RefreshCommand = Stub();
        SearchCommand = Stub();
        OpenEmailCommand = Stub();
        LinkToProjectCommand = Stub();
        CreateTaskFromEmailCommand = Stub();
        MarkHandledCommand = Stub();
        ArchiveCommand = Stub();
        ReplyCommand = Stub();
        ForwardCommand = Stub();
        OpenAttachmentCommand = Stub();
        CompleteTaskCommand = Stub();
    }

    /// <summary>Window title, mirrors the legacy email management window.</summary>
    public string Title => "\u05E0\u05D9\u05D4\u05D5\u05DC \u05D3\u05D5\u05D0\u05E8 \u2014 \u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9"; // "Email management — visual shell"

    /// <summary>
    /// Shared, reusable Project Selector hosted by this window. Email does not own project selection;
    /// selecting a project here publishes it to the shared <see cref="ICurrentProjectContext"/>.
    /// </summary>
    public ProjectSelectorViewModel ProjectSelector { get; }

    /// <summary>Project/context label shown in the selected-project info strip; follows the Current Project.</summary>
    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public ObservableCollection<EmailFolderRow> Folders { get; }

    public ObservableCollection<string> StatusOptions { get; }

    public ObservableCollection<EmailListRow> Emails { get; }

    public ObservableCollection<EmailAttachmentRow> Attachments { get; }

    /// <summary>Free-text search box value (bound, but no search is performed in this slice).</summary>
    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public EmailFolderRow? SelectedFolder
    {
        get => _selectedFolder;
        set => SetField(ref _selectedFolder, value);
    }

    public string? SelectedStatus
    {
        get => _selectedStatus;
        set => SetField(ref _selectedStatus, value);
    }

    public EmailListRow? SelectedEmail
    {
        get => _selectedEmail;
        set
        {
            if (SetField(ref _selectedEmail, value))
            {
                OnPropertyChanged(nameof(HasSelectedEmail));
                OnPropertyChanged(nameof(SelectedEmailBody));
            }
        }
    }

    /// <summary>True when an email is selected; drives the viewer/empty-state visibility.</summary>
    public bool HasSelectedEmail => _selectedEmail is not null;

    /// <summary>Fake plain-text body preview for the selected email (design-time only).</summary>
    public string SelectedEmailBody =>
        _selectedEmail is null ? string.Empty : EmailWindowDesignData.SampleBody;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand OpenEmailCommand { get; }
    public ICommand LinkToProjectCommand { get; }
    public ICommand CreateTaskFromEmailCommand { get; }
    public ICommand MarkHandledCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand ReplyCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand OpenAttachmentCommand { get; }
    public ICommand CompleteTaskCommand { get; }

    /// <summary>
    /// Placeholder hook for the workflow-first open path. A later slice will project the task's
    /// project/email into the header and (read-only) data; for the visual-clone slice it only records
    /// that a context was supplied. No workflow is started, advanced, or mutated here.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context)
    {
        if (context is null)
        {
            return;
        }

        StatusMessage =
            "\u05E0\u05E4\u05EA\u05D7 \u05DE\u05EA\u05D5\u05DA \u05DE\u05E9\u05D9\u05DE\u05D4 (\u05D7\u05D9\u05D1\u05D5\u05E8 \u05E0\u05EA\u05D5\u05E0\u05D9\u05DD \u05D9\u05D5\u05E9\u05DC\u05DD \u05D1\u05D4\u05DE\u05E9\u05DA)"; // "Opened from a task (data wiring to follow)"
    }

    private AsyncRelayCommand Stub() => new(() =>
    {
        StatusMessage = NotWiredYet;
        return Task.CompletedTask;
    });

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
        => UpdateActiveProjectDisplay(e.Project);

    private void UpdateActiveProjectDisplay(ProjectSummaryDto? project)
    {
        ActiveProjectDisplay = project is null
            ? "\u05DC\u05D0 \u05E0\u05D1\u05D7\u05E8 \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8" // "No project selected"
            : $"{project.ProjectNumber} \u2014 {project.ProjectName}";
    }

    /// <summary>Unsubscribes from the shared context and disposes the hosted selector to avoid leaks.</summary>
    public void Dispose()
    {
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;
        ProjectSelector.Dispose();
    }
}
