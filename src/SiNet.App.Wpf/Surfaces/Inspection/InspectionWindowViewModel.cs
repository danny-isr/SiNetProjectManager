using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// View model for <see cref="InspectionWindowView"/> — the visual clone of the legacy
/// <c>FloatingInspectionView</c> (window title "\u05D3\u05D5\u05D7\u05D5\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA").
/// <para>
/// <b>Visual-clone slice only.</b> This view model is intentionally thin: it exposes the same
/// bindable surface (title, pin/dock/collapse state, templates, reports, notes, status) and the same
/// command names as the old screen so the window looks and feels identical, but it carries
/// <b>no</b> heavy legacy logic. It does NOT touch the database, generate reports, pull Gmail/planner
/// responses, open ACC/files, or mutate workflow. Every command is stubbed: it simply reports
/// "not wired yet" via <see cref="StatusMessage"/>. Data is fake/design-time only
/// (<see cref="InspectionWindowDesignData"/>).
/// </para>
/// <para>
/// Workflow-first direction is preserved structurally: the window can later be opened from a
/// Workflow/Task with a <see cref="WorkSurfaceContext"/> (see <see cref="ApplyContext"/>), after which
/// individual actions will be reconnected one at a time through clean Application services. This slice
/// does not implement task opening behavior.
/// </para>
/// <para>
/// This is the visual-clone target. <c>InspectionShellView</c> remains a developer harness only.
/// </para>
/// </summary>
public sealed class InspectionWindowViewModel : ObservableObject
{
    private const string NotWiredYet =
        "\u05E4\u05E2\u05D5\u05DC\u05D4 \u05D6\u05D5 \u05D8\u05E8\u05DD \u05D7\u05D5\u05D1\u05E8\u05D4 (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u05D1\u05DC\u05D1\u05D3)."; // "This action is not wired yet (visual shell only)."

    private string _activeProjectDisplay = "\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05D3\u05D5\u05D7\u05D5\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA"; // "Sample project — Inspection reports"
    private bool _isPinned;
    private bool _isDocked;
    private bool _isCollapsed;
    private InspectionTemplateRow? _selectedTemplate;
    private InspectionReportRow? _selectedReport;
    private string _statusMessage = "\u05DE\u05D5\u05DB\u05DF (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u2014 \u05DC\u05DC\u05D0 \u05D7\u05D9\u05D1\u05D5\u05E8 \u05E0\u05EA\u05D5\u05E0\u05D9\u05DD)"; // "Ready (visual shell — no data connected)"

    public InspectionWindowViewModel()
    {
        AvailableTemplates = new ObservableCollection<InspectionTemplateRow>(InspectionWindowDesignData.SampleTemplates);
        Reports = new ObservableCollection<InspectionReportRow>(InspectionWindowDesignData.SampleReports);
        Notes = new ObservableCollection<InspectionNoteRow>(InspectionWindowDesignData.SampleNotes);
        InspectionTree = new ObservableCollection<InspectionChapterItem>(InspectionWindowDesignData.BuildSampleTree());
        StatusOptions = new ObservableCollection<string>(
        [
            "\u05E4\u05EA\u05D5\u05D7\u05D4",       // Open
            "\u05D3\u05D5\u05E8\u05E9 \u05EA\u05D9\u05E7\u05D5\u05DF", // Requires correction
            "\u05D8\u05D5\u05E4\u05DC",       // Handled
            "\u05DC\u05D0 \u05E8\u05DC\u05D5\u05D5\u05E0\u05D8\u05D9",   // Not relevant
        ]);

        _selectedTemplate = AvailableTemplates.FirstOrDefault();
        _selectedReport = Reports.FirstOrDefault();

        ToggleCollapseCommand = new AsyncRelayCommand(() =>
        {
            IsCollapsed = !IsCollapsed;
            return Task.CompletedTask;
        });

        RefreshCommand = Stub();
        CreateReportCommand = Stub();
        MarkResponseReceivedCommand = Stub();
        RepullPlannerResponsesCommand = Stub();
        OpenSourceReportCommand = Stub();
        UnlockReportCommand = Stub();
        ShareReportCommand = Stub();
        SelectReviewedPlanCommand = Stub();
        AddNoteCommand = Stub();
        MoveNoteUpCommand = Stub();
        MoveNoteDownCommand = Stub();
        ScreenshotPrimaryCommand = Stub();
        OpenNoteLinkedFileCommand = Stub();
        CompleteTaskCommand = Stub();
    }

    /// <summary>Window title, mirrors the legacy floating inspection window.</summary>
    public string Title => "\u05D3\u05D5\u05D7\u05D5\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA"; // "Inspection reports"

    /// <summary>Project name shown in the green header bar.</summary>
    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    public bool IsDocked
    {
        get => _isDocked;
        set => SetField(ref _isDocked, value);
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetField(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
    }

    /// <summary>Inverse of <see cref="IsCollapsed"/>; body panels are visible only when expanded.</summary>
    public bool IsExpanded => !_isCollapsed;

    public ObservableCollection<InspectionTemplateRow> AvailableTemplates { get; }

    public InspectionTemplateRow? SelectedTemplate
    {
        get => _selectedTemplate;
        set => SetField(ref _selectedTemplate, value);
    }

    public ObservableCollection<InspectionReportRow> Reports { get; }

    public InspectionReportRow? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetField(ref _selectedReport, value))
            {
                OnPropertyChanged(nameof(HasSelectedReport));
            }
        }
    }

    /// <summary><see langword="true"/> when a report card is selected (drives body visibility).</summary>
    public bool HasSelectedReport => _selectedReport is not null;

    public ObservableCollection<InspectionNoteRow> Notes { get; }

    /// <summary>
    /// Fake/design-time questionnaire tree (Chapter -> Section -> Note) for the hierarchical notes
    /// area. Visual-only; no DB, no <c>IInspectionReportService</c>.
    /// </summary>
    public ObservableCollection<InspectionChapterItem> InspectionTree { get; }

    /// <summary>Fake status options for the per-note status combo (matches legacy <c>StatusOptions</c>).</summary>
    public ObservableCollection<string> StatusOptions { get; }

    /// <summary>Status line shown in the bottom bar; also used by stubbed commands.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ICommand ToggleCollapseCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CreateReportCommand { get; }
    public ICommand MarkResponseReceivedCommand { get; }
    public ICommand RepullPlannerResponsesCommand { get; }
    public ICommand OpenSourceReportCommand { get; }
    public ICommand UnlockReportCommand { get; }
    public ICommand ShareReportCommand { get; }
    public ICommand SelectReviewedPlanCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand MoveNoteUpCommand { get; }
    public ICommand MoveNoteDownCommand { get; }
    public ICommand ScreenshotPrimaryCommand { get; }
    public ICommand OpenNoteLinkedFileCommand { get; }
    public ICommand CompleteTaskCommand { get; }

    /// <summary>
    /// Placeholder hook for the workflow-first open path. A later slice will project the task's
    /// project/report into the header and (read-only) data; for the visual-clone slice it only
    /// records that a context was supplied. No workflow is started, advanced, or mutated here.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context)
    {
        if (context is null)
        {
            return;
        }

        StatusMessage = "\u05E0\u05E4\u05EA\u05D7 \u05DE\u05EA\u05D5\u05DA \u05DE\u05E9\u05D9\u05DE\u05D4 (\u05D7\u05D9\u05D1\u05D5\u05E8 \u05E0\u05EA\u05D5\u05E0\u05D9\u05DD \u05D9\u05D5\u05E9\u05DC\u05DD \u05D1\u05D4\u05DE\u05E9\u05DA)"; // "Opened from a task (data wiring to follow)"
    }

    private AsyncRelayCommand Stub() => new(() =>
    {
        StatusMessage = NotWiredYet;
        return Task.CompletedTask;
    });
}
