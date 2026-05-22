using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2.Converters;
using SiNetSQL.Services;
using SiNetSQL.Services.Health;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// Persistent System Health indicator surfaced in MainWindow's top bar.
/// <para>
/// Primary UX: a tiny colored dot + label that summarises the worst service state.
/// Hover or click opens a compact <see cref="System.Windows.Controls.Primitives.Popup"/>
/// listing all services. Closes on outside-click (StaysOpen=false) or pointer-leave.
/// </para>
/// </summary>
public partial class SystemHealthIndicator : UserControl
{
    private ISystemHealthService? _health;
    private readonly ObservableCollection<HealthRowVm> _rows = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimerHelper? _autoCloseTimer;

    public SystemHealthIndicator()
    {
        InitializeComponent();
        RowsList.ItemsSource = _rows;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_health is not null) return;
        try
        {
            _health = App.ServiceProvider.GetRequiredService<ISystemHealthService>();
        }
        catch
        {
            // DI not ready yet — indicator stays gray until next layout pass.
            return;
        }

        _health.StatusChanged += OnStatusChanged;

        // Seed from current snapshot.
        foreach (var s in _health.Current.Values)
            UpsertRow(s);
        UpdateAggregate();

        // Kick a first refresh in the background — no UI block, no OAuth, read-only.
        _ = Task.Run(async () =>
        {
            try { await _health.RefreshAllAsync(CancellationToken.None); }
            catch { /* swallowed: AppLogger inside service captures meaningful failures */ }
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_health is not null)
            _health.StatusChanged -= OnStatusChanged;
        _cts?.Cancel();
        _autoCloseTimer?.Stop();
    }

    private void OnStatusChanged(ServiceHealthStatus s)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var prev = ComputeAggregate();
            UpsertRow(s);
            UpdateAggregate();
            var now = ComputeAggregate();
            AppLogger.Info($"[Health][Indicator] OnStatusChanged key={s.Key} state={s.State} critical={s.IsCritical} aggregate {prev} -> {now}");
        }));
    }

    private void UpsertRow(ServiceHealthStatus s)
    {
        var existing = _rows.FirstOrDefault(r => r.Key == s.Key);
        if (existing is null)
        {
            _rows.Add(HealthRowVm.From(s));
        }
        else
        {
            existing.Update(s);
        }
        LastUpdatedText.Text = "עודכן: " + DateTime.Now.ToString("HH:mm:ss");
    }

    private void UpdateAggregate()
    {
        var aggregate = ComputeAggregate();
        StatusDot.Fill = ServiceHealthStateToBrushConverter.BrushFor(aggregate);
    }

    /// <summary>
    /// Authoritative recompute: rebuilds rows from <see cref="ISystemHealthService.Current"/>
    /// and updates the aggregate brush. Used after a manual "בדוק עכשיו" so the top dot
    /// can never lag behind the per-row colors.
    /// </summary>
    private void RebuildFromCurrent()
    {
        if (_health is null) return;
        var snapshot = _health.Current;
        foreach (var s in snapshot.Values)
            UpsertRow(s);
        // Drop rows whose key no longer exists (defensive).
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            if (!snapshot.ContainsKey(_rows[i].Key))
                _rows.RemoveAt(i);
        }
        var agg = ComputeAggregate();
        StatusDot.Fill = ServiceHealthStateToBrushConverter.BrushFor(agg);
        var dump = string.Join(", ", snapshot.Values.Select(v => $"{v.Key}={v.State}{(v.IsCritical?"*":"")}"));
        AppLogger.Info($"[Health][Indicator] RebuildFromCurrent aggregate={agg} rows=[{dump}]");
    }

    /// <summary>Worst-state policy: Offline (critical) > Offline (any) > Warning/Auth > NotConfigured(critical) > Online > Unknown/Checking.</summary>
    private ServiceHealthState ComputeAggregate()
    {
        if (_rows.Count == 0) return ServiceHealthState.Unknown;

        bool anyCriticalOffline = _rows.Any(r => r.IsCritical && r.State == ServiceHealthState.Offline);
        if (anyCriticalOffline) return ServiceHealthState.Offline;

        bool anyWarn = _rows.Any(r =>
            r.State == ServiceHealthState.Warning ||
            r.State == ServiceHealthState.RequiresAuthorization ||
            (r.IsCritical && r.State == ServiceHealthState.NotConfigured) ||
            (!r.IsCritical && r.State == ServiceHealthState.Offline));
        if (anyWarn) return ServiceHealthState.Warning;

        bool allOnline = _rows.All(r =>
            r.State == ServiceHealthState.Online ||
            (!r.IsCritical && r.State == ServiceHealthState.NotConfigured));
        if (allOnline) return ServiceHealthState.Online;

        return ServiceHealthState.Unknown;
    }

    // ── Hover open / leave close ────────────────────────────────────────────
    private void Host_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCloseTimer?.Stop();
        IndicatorButton.IsChecked = true;
    }

    private void Host_MouseLeave(object sender, MouseEventArgs e)
    {
        // Defer close so the user can move from the indicator into the popup.
        _autoCloseTimer ??= new DispatcherTimerHelper(TimeSpan.FromMilliseconds(250), TryCloseIfOutside);
        _autoCloseTimer.Restart();
    }

    private void Popup_MouseLeave(object sender, MouseEventArgs e)
    {
        _autoCloseTimer ??= new DispatcherTimerHelper(TimeSpan.FromMilliseconds(250), TryCloseIfOutside);
        _autoCloseTimer.Restart();
    }

    private void TryCloseIfOutside()
    {
        if (HostGrid.IsMouseOver || StatusPopup.IsMouseOver) return;
        IndicatorButton.IsChecked = false;
    }

    // ── Buttons ────────────────────────────────────────────────────────────
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_health is null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        RefreshButton.IsEnabled = false;
        try
        {
            await _health.RefreshAllAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            RefreshButton.IsEnabled = true;
            // Authoritative recompute from Current — guarantees the top dot reflects
            // the latest snapshot even if a per-service StatusChanged was missed/coalesced.
            try { RebuildFromCurrent(); }
            catch (Exception ex) { AppLogger.Warn($"[Health][Indicator] RebuildFromCurrent failed: {ex.Message}"); }
        }
    }

    private void OpenDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_health is null) return;
        // Optional secondary entry — the existing SystemHealthWindow stays as a temporary detail view.
        // It is no longer the primary UX (the persistent indicator + popup is).
        var win = new Views.SystemHealthWindow(_health)
        {
            Owner = Window.GetWindow(this)
        };
        win.Show();
        IndicatorButton.IsChecked = false;
    }
}

internal sealed class DispatcherTimerHelper
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public DispatcherTimerHelper(TimeSpan interval, Action tick)
    {
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => { _timer.Stop(); tick(); };
    }

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}

public sealed class HealthRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; init; } = "";
    public string DisplayName { get; private set; } = "";
    public string Message { get; private set; } = "";
    public string? TechnicalDetails { get; private set; }
    public string LastCheckedLocal { get; private set; } = "";
    public ServiceHealthState State { get; private set; } = ServiceHealthState.Unknown;
    public bool IsCritical { get; private set; }

    public static HealthRowVm From(ServiceHealthStatus s)
    {
        var row = new HealthRowVm { Key = s.Key };
        row.Update(s);
        return row;
    }

    public void Update(ServiceHealthStatus s)
    {
        DisplayName = s.DisplayName;
        Message = s.Message ?? "";
        TechnicalDetails = s.TechnicalDetails;
        State = s.State;
        IsCritical = s.IsCritical;
        LastCheckedLocal = (s.LastCheckedUtc is null || s.LastCheckedUtc == default(DateTime))
            ? ""
            : s.LastCheckedUtc.Value.ToLocalTime().ToString("HH:mm:ss");
        Raise(nameof(DisplayName));
        Raise(nameof(Message));
        Raise(nameof(TechnicalDetails));
        Raise(nameof(State));
        Raise(nameof(IsCritical));
        Raise(nameof(LastCheckedLocal));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
