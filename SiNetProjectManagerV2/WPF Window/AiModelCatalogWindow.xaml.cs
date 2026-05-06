using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Services;
using SiNetSQL.Services.AI;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Admin dialog that lists the curated <see cref="AiModelCatalog"/> alongside the live state of each
/// model (installed locally via Ollama, configured for cloud use, or available-to-install).
/// <list type="bullet">
///   <item>Local models (<see cref="AiProvider.Ollama"/>): "Download" → <c>POST /api/pull</c>.</item>
///   <item>Cloud models (<see cref="AiProvider.Gemini"/>): "Configure" → adds entry to
///         <see cref="SystemSettingKeys.AiConfiguredCloudModels"/>.</item>
/// </list>
/// Installed Ollama models are refreshed via <c>GET /api/tags</c>.
/// </summary>
public partial class AiModelCatalogWindow : Window
{
    private readonly SystemSettingsService _settings;
    private readonly ObservableCollection<CatalogRow> _rows = new();
    private HashSet<string> _installedOllama = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _configuredCloud = new(StringComparer.OrdinalIgnoreCase);
    private string _ollamaBaseUrl = "http://localhost:11434";

    public AiModelCatalogWindow()
    {
        InitializeComponent();
        _settings = App.ServiceProvider.GetRequiredService<SystemSettingsService>();

        ProviderFilterCombo.Items.Add("הכל");
        foreach (var p in Enum.GetValues<AiProvider>())
            ProviderFilterCombo.Items.Add(p.ToString());
        ProviderFilterCombo.SelectedIndex = 0;

        CatalogGrid.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _ollamaBaseUrl = await _settings.GetOrDefaultAsync(
            SystemSettingKeys.OllamaBaseUrl, "http://localhost:11434");

        var configuredCsv = await _settings.GetOrDefaultAsync(
            SystemSettingKeys.AiConfiguredCloudModels, string.Empty);
        _configuredCloud = ParseConfiguredCloud(configuredCsv);

        await RefreshInstalledOllamaAsync();
        Rebuild();
    }

    private void Rebuild()
    {
        var filter = ProviderFilterCombo.SelectedItem as string ?? "הכל";
        _rows.Clear();
        foreach (var m in AiModelCatalog.All)
        {
            if (filter != "הכל" && !string.Equals(filter, m.Provider.ToString(), StringComparison.Ordinal))
                continue;
            _rows.Add(new CatalogRow(m, GetStatus(m)));
        }
        UpdateButtons();
    }

    private CatalogStatus GetStatus(AiCatalogModel m) => m.Provider switch
    {
        AiProvider.Ollama => _installedOllama.Contains(m.ModelName)
            ? CatalogStatus.Installed
            : CatalogStatus.AvailableToDownload,
        _ => _configuredCloud.Contains(FormatCloudKey(m.Provider, m.ModelName))
            ? CatalogStatus.Configured
            : CatalogStatus.AvailableToConfigure
    };

    private static string FormatCloudKey(AiProvider provider, string modelName) => $"{provider}|{modelName}";

    private static HashSet<string> ParseConfiguredCloud(string csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var entry in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(entry);
        return set;
    }

    private async Task PersistConfiguredCloudAsync()
    {
        var csv = string.Join(",", _configuredCloud);
        await _settings.SetAsync(
            SystemSettingKeys.AiConfiguredCloudModels,
            csv,
            "מודלי ענן שהוגדרו על ידי המנהל ויופיעו ברשימות הבחירה של AI (פורמט: Provider|ModelName, מופרד בפסיקים)");
    }

    private async Task RefreshInstalledOllamaAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await client.GetStringAsync($"{_ollamaBaseUrl.TrimEnd('/')}/api/tags");
            using var doc = JsonDocument.Parse(json);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("models", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.TryGetProperty("name", out var name))
                    {
                        var n = name.GetString();
                        if (!string.IsNullOrWhiteSpace(n)) set.Add(n);
                    }
                }
            }
            _installedOllama = set;
            StatusLabel.Text = $"✅ {set.Count} מודלים מותקנים ב-Ollama";
        }
        catch (Exception ex)
        {
            _installedOllama = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StatusLabel.Text = $"⚠️ לא ניתן להגיע ל-Ollama: {ex.Message}";
        }
    }

    private async void RefreshInstalledButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshInstalledButton.IsEnabled = false;
        try
        {
            await RefreshInstalledOllamaAsync();
            Rebuild();
        }
        finally
        {
            RefreshInstalledButton.IsEnabled = true;
        }
    }

    private void ProviderFilter_Changed(object sender, SelectionChangedEventArgs e) => Rebuild();

    private void CatalogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
        if (CatalogGrid.SelectedItem is CatalogRow row)
            NotesLabel.Text = row.Model.Notes;
        else
            NotesLabel.Text = string.Empty;
    }

    private void UpdateButtons()
    {
        var row = CatalogGrid.SelectedItem as CatalogRow;
        if (row is null)
        {
            DownloadButton.IsEnabled = false;
            ConfigureButton.IsEnabled = false;
            RemoveConfiguredButton.IsEnabled = false;
            return;
        }

        var m = row.Model;
        DownloadButton.IsEnabled = m.IsDownloadable && row.Status == CatalogStatus.AvailableToDownload;
        ConfigureButton.IsEnabled = !m.IsDownloadable && row.Status == CatalogStatus.AvailableToConfigure;
        RemoveConfiguredButton.IsEnabled = !m.IsDownloadable && row.Status == CatalogStatus.Configured;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogGrid.SelectedItem is not CatalogRow row) return;
        if (row.Model.Provider != AiProvider.Ollama) return;

        DownloadButton.IsEnabled = false;
        StatusLabel.Text = $"⏳ מוריד {row.Model.ModelName}... זה יכול לקחת מספר דקות.";

        try
        {
            await PullOllamaModelAsync(row.Model.ModelName);
            await RefreshInstalledOllamaAsync();
            Rebuild();
            StatusLabel.Text = $"✅ {row.Model.ModelName} הותקן בהצלחה";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"❌ הורדה נכשלה: {ex.Message}";
            MessageBox.Show($"הורדת המודל נכשלה:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateButtons();
        }
    }

    private async Task PullOllamaModelAsync(string modelName)
    {
        // Stream the response so we don't pre-buffer hundreds of MB of progress JSON.
        // Each line is a separate JSON object; we only care that the stream completes
        // without an error event.
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var url = $"{_ollamaBaseUrl.TrimEnd('/')}/api/pull";
        var body = JsonSerializer.Serialize(new { name = modelName, stream = true });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new InvalidOperationException(err.GetString() ?? "Ollama pull error");
                if (doc.RootElement.TryGetProperty("status", out var st))
                {
                    var status = st.GetString();
                    if (!string.IsNullOrWhiteSpace(status))
                        Dispatcher.Invoke(() => StatusLabel.Text = $"⏳ {modelName}: {status}");
                }
            }
            catch (JsonException)
            {
                // Skip malformed progress lines — Ollama's stream is mostly progress chunks.
            }
        }
    }

    private async void ConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogGrid.SelectedItem is not CatalogRow row) return;
        var key = FormatCloudKey(row.Model.Provider, row.Model.ModelName);
        if (_configuredCloud.Add(key))
        {
            await PersistConfiguredCloudAsync();
            Rebuild();
            StatusLabel.Text = $"✅ {row.Model.DisplayName} הוגדר ויופיע ברשימת הבחירה";
        }
    }

    private async void RemoveConfiguredButton_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogGrid.SelectedItem is not CatalogRow row) return;
        var key = FormatCloudKey(row.Model.Provider, row.Model.ModelName);
        if (_configuredCloud.Remove(key))
        {
            await PersistConfiguredCloudAsync();
            Rebuild();
            StatusLabel.Text = $"ℹ️ {row.Model.DisplayName} הוסר מההגדרות";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private enum CatalogStatus
    {
        AvailableToDownload,
        AvailableToConfigure,
        Installed,
        Configured
    }

    private sealed class CatalogRow(AiCatalogModel model, CatalogStatus status)
    {
        public AiCatalogModel Model { get; } = model;
        public CatalogStatus Status { get; } = status;

        // DataGrid binding properties
        public string DisplayName => Model.DisplayName;
        public string ModelName => Model.ModelName;
        public string Provider => Model.Provider.ToString();
        public string RecommendedLevel => Model.RecommendedLevel.ToString();
        public bool SupportsHebrew => Model.SupportsHebrew;

        public string StatusText => Status switch
        {
            CatalogStatus.Installed => "✅ מותקן",
            CatalogStatus.Configured => "✅ מוגדר",
            CatalogStatus.AvailableToDownload => "⬇ זמין להורדה",
            CatalogStatus.AvailableToConfigure => "להגדרה",
            _ => string.Empty
        };
    }
}
