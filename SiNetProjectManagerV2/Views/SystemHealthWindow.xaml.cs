using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SiNetSQL.Services.Health;

namespace SiNetProjectManagerV2.Views;

/// <summary>
/// Optional detail window for system health.
/// <para>
/// NOTE: This is no longer the primary UX for displaying system health. The primary UX is
/// <see cref="SiNetProjectManagerV2.Controls.SystemHealthIndicator"/> — a persistent dot
/// in MainWindow's top bar that opens a compact popup. This window is kept temporarily as
/// an optional detail / inspection view (opened from the popup's "פתח פירוט" button or
/// from the secondary "מצב מערכת (חלון פירוט)" menu entry). Candidate for removal in a
/// later round, only after the indicator UX has been validated in production.
/// </para>
/// </summary>
public partial class SystemHealthWindow : Window
{
    private readonly ISystemHealthService _health;
    private readonly ObservableCollection<HealthRow> _rows = new();
    private CancellationTokenSource? _cts;

    public SystemHealthWindow(ISystemHealthService health)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        InitializeComponent();
        StatusGrid.ItemsSource = _rows;

        _health.StatusChanged += OnStatusChanged;
        Loaded += async (_, _) => await RunRefreshAsync();
        Closed += (_, _) =>
        {
            _health.StatusChanged -= OnStatusChanged;
            _cts?.Cancel();
        };
    }

    private void OnStatusChanged(ServiceHealthStatus e)
    {
        Dispatcher.BeginInvoke(new Action(() => UpsertRow(e)));
    }

    private void UpsertRow(ServiceHealthStatus s)
    {
        var existing = _rows.FirstOrDefault(r => r.Key == s.Key);
        if (existing is null)
        {
            _rows.Add(HealthRow.From(s));
        }
        else
        {
            existing.Update(s);
        }
        LastUpdatedText.Text = "עודכן לאחרונה: " + DateTime.Now.ToString("HH:mm:ss");
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunRefreshAsync();
    }

    private async Task RunRefreshAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        RefreshButton.IsEnabled = false;
        try
        {
            // Seed grid with current snapshot first so the user sees something immediately.
            foreach (var s in _health.Current.Values)
                UpsertRow(s);

            await _health.RefreshAllAsync(_cts.Token);
        }
        catch (OperationCanceledException) { /* user closed window */ }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}

public sealed class HealthRow : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; init; } = "";
    public string DisplayName { get; private set; } = "";
    public string Category { get; private set; } = "";
    public string Message { get; private set; } = "";
    public string StateGlyph { get; private set; } = "";
    public string LastCheckedLocal { get; private set; } = "";
    public ServiceHealthState State { get; private set; } = ServiceHealthState.Unknown;

    public static HealthRow From(ServiceHealthStatus s)
    {
        var row = new HealthRow { Key = s.Key };
        row.Update(s);
        return row;
    }

    public void Update(ServiceHealthStatus s)
    {
        DisplayName = s.DisplayName;
        Category = s.Category;
        Message = s.Message ?? "";
        State = s.State;
        StateGlyph = GlyphFor(s.State);
        LastCheckedLocal = (s.LastCheckedUtc is null || s.LastCheckedUtc == default(DateTime))
            ? ""
            : s.LastCheckedUtc.Value.ToLocalTime().ToString("HH:mm:ss");
        Raise(nameof(DisplayName));
        Raise(nameof(Category));
        Raise(nameof(Message));
        Raise(nameof(State));
        Raise(nameof(StateGlyph));
        Raise(nameof(LastCheckedLocal));
    }

    private static string GlyphFor(ServiceHealthState state) => state switch
    {
        ServiceHealthState.Online => "🟢",
        ServiceHealthState.Warning => "🟡",
        ServiceHealthState.Offline => "🔴",
        ServiceHealthState.RequiresAuthorization => "🔑",
        ServiceHealthState.NotConfigured => "⚪",
        ServiceHealthState.Checking => "⏳",
        _ => "❔",
    };

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
