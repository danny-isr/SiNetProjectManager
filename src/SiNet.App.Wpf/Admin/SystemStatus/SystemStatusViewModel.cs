using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SiNet.App.Wpf.Admin.UserGroups;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Runtime;

namespace SiNet.App.Wpf.Admin.SystemStatus;

public sealed class SystemStatusViewModel : ObservableObject
{
    private readonly IRuntimeSubsystemStatusService _runtime;
    private readonly IUserGroupsWindowFactory? _userGroupsWindowFactory;
    private bool _isBusy;
    private string _summary = "טוען…";

    public SystemStatusViewModel(
        IRuntimeSubsystemStatusService runtime,
        IUserGroupsWindowFactory? userGroupsWindowFactory = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _userGroupsWindowFactory = userGroupsWindowFactory;
        Rows = new ObservableCollection<SystemStatusRowViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenUserGroupsCommand = new RelayCommand(
            _ => OpenUserGroups(),
            _ => _userGroupsWindowFactory is not null);
        _runtime.Changed += OnRuntimeChanged;
        ApplySnapshot(_runtime.Current);
    }

    public ObservableCollection<SystemStatusRowViewModel> Rows { get; }

    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshCommand { get; }
    public RelayCommand OpenUserGroupsCommand { get; }

    public async Task LoadAsync() => await RefreshAsync().ConfigureAwait(true);

    public void Dispose() => _runtime.Changed -= OnRuntimeChanged;

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await _runtime.RefreshAsync().ConfigureAwait(true);
            ApplySnapshot(_runtime.Current);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnRuntimeChanged(object? sender, EventArgs e) =>
        ApplySnapshot(_runtime.Current);

    private void ApplySnapshot(IReadOnlyList<SubsystemRuntimeStatus> statuses)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            var snapshot = statuses;
            dispatcher.BeginInvoke(() => ApplySnapshot(snapshot), DispatcherPriority.Background);
            return;
        }

        Rows.Clear();
        foreach (var s in statuses)
            Rows.Add(SystemStatusRowViewModel.From(s));

        var running = statuses.Count(s => s.State == SubsystemRuntimeState.Running);
        var degraded = statuses.Count(s =>
            s.State is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.Stopped);
        var bg = statuses.Sum(s => s.ActiveWorkCount ?? 0);
        var ok = statuses.Count(s => s.State is SubsystemRuntimeState.Idle or SubsystemRuntimeState.Running);
        Summary = $"{ok} פעילים/מוכנים · {running} רצים ברקע · {degraded} בתקלה · עבודת רקע: {bg}";
    }

    private void OpenUserGroups()
    {
        if (_userGroupsWindowFactory is null)
            return;

        try
        {
            ThemeResourceLoader.EnsureApplicationResourcesMerged();
            var window = _userGroupsWindowFactory.Create();
            var owner = System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive);
            if (owner is not null)
                window.Owner = owner;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"פתיחת הקצאות נכשלה: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

public sealed class SystemStatusRowViewModel
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string StateLabel { get; init; }
    public required string Summary { get; init; }
    public required string Guidance { get; init; }
    public bool HasGuidance => !string.IsNullOrWhiteSpace(Guidance);
    public required string ActiveWorkDisplay { get; init; }
    public required Brush StateBrush { get; init; }

    public static SystemStatusRowViewModel From(SubsystemRuntimeStatus s)
    {
        var enriched = SystemStatusGuidanceCatalog.WithGuidance(s);
        return new SystemStatusRowViewModel
        {
            Key = enriched.Key,
            DisplayName = enriched.DisplayNameHe,
            StateLabel = StateToHe(enriched.State),
            Summary = enriched.SummaryHe,
            Guidance = enriched.GuidanceHe ?? string.Empty,
            ActiveWorkDisplay = enriched.ActiveWorkCount is > 0
                ? enriched.ActiveWorkCount.Value.ToString()
                : "—",
            StateBrush = StateToBrush(enriched.State),
        };
    }

    private static string StateToHe(SubsystemRuntimeState state) => state switch
    {
        SubsystemRuntimeState.Running => "רץ",
        SubsystemRuntimeState.Idle => "מוכן",
        SubsystemRuntimeState.Degraded => "מוגבל",
        SubsystemRuntimeState.Stopped => "כבוי",
        SubsystemRuntimeState.NotConfigured => "לא מוגדר",
        _ => state.ToString(),
    };

    private static Brush StateToBrush(SubsystemRuntimeState state)
    {
        var color = state switch
        {
            SubsystemRuntimeState.Running => Color.FromRgb(0x15, 0x65, 0xC0),
            SubsystemRuntimeState.Idle => Color.FromRgb(0x2E, 0x7D, 0x32),
            SubsystemRuntimeState.Degraded => Color.FromRgb(0xEF, 0x6C, 0x00),
            SubsystemRuntimeState.Stopped => Color.FromRgb(0xC6, 0x28, 0x28),
            _ => Color.FromRgb(0x75, 0x75, 0x75),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
