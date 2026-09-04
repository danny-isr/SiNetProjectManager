using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Runtime;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// View model for the clean New System shell (see <c>docs/APP_SHELL.md</c> §1/§6).
/// Identity coherence drives <see cref="IdentityStatusText"/> (AutomationId Shell.IdentityStatus).
/// </summary>
public class NewShellViewModel : INotifyPropertyChanged, IDisposable
{
    private const string DefaultStatusText = "מוכן";
    private const string NoProjectText = "לא נבחר פרויקט";

    private static readonly Brush DefaultHealthBrush = CreateFrozenBrush(0x9E, 0x9E, 0x9E);

    private readonly ICurrentProjectContext? _currentProjectContext;
    private readonly IRuntimeSubsystemStatusService? _runtimeStatus;
    private readonly IIdentityCoherenceService? _identityCoherence;
    private readonly Action? _openSystemStatus;

    private string _currentUserDisplay;
    private string _currentProjectDisplay;
    private string _windowTitle;
    private string _statusText;
    private string _identityStatusText = "זהות: בודק…";
    private string _identityStatusToolTip = string.Empty;
    private Brush _overallHealthBrush;
    private int _activeBackgroundWorkCount;
    private object? _currentContent;

    public NewShellViewModel(
        IEnumerable<NewShellMenuItem> menuItems,
        string? currentUserDisplay,
        ICurrentProjectContext? currentProjectContext = null,
        string? currentProjectDisplay = null,
        Action? openNewProject = null,
        IRuntimeSubsystemStatusService? runtimeStatus = null,
        Action? openSystemStatus = null,
        IIdentityCoherenceService? identityCoherence = null)
    {
        ArgumentNullException.ThrowIfNull(menuItems);

        MenuItems = new ObservableCollection<NewShellMenuItem>(menuItems);
        _currentUserDisplay = string.IsNullOrWhiteSpace(currentUserDisplay)
            ? "משתמש לא ידוע"
            : currentUserDisplay;
        _currentProjectContext = currentProjectContext;
        _runtimeStatus = runtimeStatus;
        _identityCoherence = identityCoherence;
        _openSystemStatus = openSystemStatus;
        _windowTitle = NewShellWindowTitle.Format(_currentProjectContext?.CurrentProject);
        _currentProjectDisplay = string.IsNullOrWhiteSpace(currentProjectDisplay)
            ? NoProjectText
            : currentProjectDisplay!;
        _statusText = DefaultStatusText;
        _overallHealthBrush = DefaultHealthBrush;

        CanOpenNewProject = openNewProject is not null;
        OpenNewProjectCommand = new RelayCommand(_ => openNewProject?.Invoke(), _ => CanOpenNewProject);
        CanOpenSystemStatus = openSystemStatus is not null;
        OpenSystemStatusCommand = new RelayCommand(_ => _openSystemStatus?.Invoke(), _ => CanOpenSystemStatus);

        if (_currentProjectContext is not null)
        {
            ApplyProject(_currentProjectContext.CurrentProject);
            _currentProjectContext.CurrentProjectChanged += OnCurrentProjectChanged;
        }

        if (_runtimeStatus is not null)
        {
            ApplyRuntimeStatus(_runtimeStatus.Current);
            _runtimeStatus.Changed += OnRuntimeStatusChanged;
        }

        if (_identityCoherence is not null)
        {
            ApplyIdentitySnapshot(_identityCoherence.Current);
            _identityCoherence.Changed += OnIdentityChanged;
        }
    }

    public string Title => "שיא חדש בע״מ";

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetField(ref _windowTitle, value);
    }

    public string HeaderSubtitle => "מנהל פרויקטים · המערכת החדשה";

    public ObservableCollection<NewShellMenuItem> MenuItems { get; }

    public object? CurrentContent
    {
        get => _currentContent;
        set
        {
            if (EqualityComparer<object?>.Default.Equals(_currentContent, value))
            {
                return;
            }

            _currentContent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasContent)));
        }
    }

    public bool HasContent => CurrentContent is not null;

    public string CurrentUserDisplay
    {
        get => _currentUserDisplay;
        private set => SetField(ref _currentUserDisplay, value);
    }

    public string CurrentProjectDisplay
    {
        get => _currentProjectDisplay;
        private set => SetField(ref _currentProjectDisplay, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    /// <summary>Footer identity line (AutomationId Shell.IdentityStatus).</summary>
    public string IdentityStatusText
    {
        get => _identityStatusText;
        private set => SetField(ref _identityStatusText, value);
    }

    public string IdentityStatusToolTip
    {
        get => _identityStatusToolTip;
        private set => SetField(ref _identityStatusToolTip, value);
    }

    public Brush OverallHealthBrush
    {
        get => _overallHealthBrush;
        private set => SetField(ref _overallHealthBrush, value);
    }

    public int ActiveBackgroundWorkCount
    {
        get => _activeBackgroundWorkCount;
        private set => SetField(ref _activeBackgroundWorkCount, value);
    }

    public bool CanOpenNewProject { get; }

    public ICommand OpenNewProjectCommand { get; }

    public bool CanOpenSystemStatus { get; }

    public ICommand OpenSystemStatusCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_currentProjectContext is not null)
        {
            _currentProjectContext.CurrentProjectChanged -= OnCurrentProjectChanged;
        }

        if (_runtimeStatus is not null)
        {
            _runtimeStatus.Changed -= OnRuntimeStatusChanged;
        }

        if (_identityCoherence is not null)
        {
            _identityCoherence.Changed -= OnIdentityChanged;
        }
    }

    internal void ApplyProject(ProjectSummaryDto? project)
    {
        WindowTitle = NewShellWindowTitle.Format(project);
        var header = NewShellWindowTitle.FormatHeaderDisplay(project);
        CurrentProjectDisplay = string.IsNullOrWhiteSpace(header) ? NoProjectText : header;
    }

    internal void ApplyIdentitySnapshot(IdentityCoherenceSnapshot snapshot)
    {
        IdentityStatusText = IdentityStatusDisplay.FormatFooter(snapshot);
        IdentityStatusToolTip = IdentityStatusDisplay.FormatDetailsTooltip(snapshot);
    }

    internal void ApplyRuntimeStatus(IReadOnlyList<SubsystemRuntimeStatus> statuses)
    {
        var list = statuses ?? [];
        var bg = list.Sum(s => s.ActiveWorkCount ?? 0);
        var ok = list.Count(s => s.State is SubsystemRuntimeState.Idle or SubsystemRuntimeState.Running);
        var worst = list.Count == 0
            ? SubsystemRuntimeState.NotConfigured
            : list.OrderByDescending(s => SeverityRank(s.State)).First().State;
        ActiveBackgroundWorkCount = bg;
        OverallHealthBrush = StateToBrush(worst);

        if (list.Count == 0)
        {
            StatusText = DefaultStatusText;
            return;
        }

        if (worst is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.Stopped)
        {
            var bad = list.FirstOrDefault(s => s.State == worst);
            StatusText = bad is null
                ? "יש תקלה במערכת"
                : $"{bad.DisplayNameHe}: {bad.SummaryHe}";
            return;
        }

        StatusText = bg > 0
            ? $"{ok} תקינים · {bg} רקע פעיל"
            : $"{ok} תקינים · מוכן";
    }

    private void OnIdentityChanged(IdentityCoherenceSnapshot snapshot)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyIdentitySnapshot(snapshot), DispatcherPriority.Background);
            return;
        }

        ApplyIdentitySnapshot(snapshot);
    }

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        void Apply()
        {
            ApplyProject(e.Project);
            if (_identityCoherence is null)
            {
                return;
            }

            var projectId = e.Project?.ProjectId;
            _ = _identityCoherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(
                DisconnectGoogleOnMismatch: true,
                ProbeAccMembership: projectId is > 0,
                SiProjectId: projectId is > 0 ? projectId : null,
                HasActiveProject: projectId is > 0));
        }

        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Apply, DispatcherPriority.Background);
            return;
        }

        Apply();
    }

    private void OnRuntimeStatusChanged(object? sender, EventArgs e)
    {
        var snapshot = _runtimeStatus?.Current ?? [];
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyRuntimeStatus(snapshot), DispatcherPriority.Background);
            return;
        }

        ApplyRuntimeStatus(snapshot);
    }

    private static int SeverityRank(SubsystemRuntimeState state) => state switch
    {
        SubsystemRuntimeState.Stopped => 4,
        SubsystemRuntimeState.Degraded => 3,
        SubsystemRuntimeState.NotConfigured => 2,
        SubsystemRuntimeState.Running => 1,
        SubsystemRuntimeState.Idle => 0,
        _ => 0,
    };

    private static Brush StateToBrush(SubsystemRuntimeState state)
    {
        var (r, g, b) = state switch
        {
            SubsystemRuntimeState.Running => ((byte)0x15, (byte)0x65, (byte)0xC0),
            SubsystemRuntimeState.Idle => ((byte)0x2E, (byte)0x7D, (byte)0x32),
            SubsystemRuntimeState.Degraded => ((byte)0xEF, (byte)0x6C, (byte)0x00),
            SubsystemRuntimeState.Stopped => ((byte)0xC6, (byte)0x28, (byte)0x28),
            _ => ((byte)0x75, (byte)0x75, (byte)0x75),
        };
        return CreateFrozenBrush(r, g, b);
    }

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
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
