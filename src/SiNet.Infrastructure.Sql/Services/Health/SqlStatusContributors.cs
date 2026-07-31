using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.DevTools;
using SiNet.Application.Runtime;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Health;

/// <summary>
/// «מצב מערכת» rows backed by the SQL layer, ported from the legacy SiNetSQL health checks so the
/// standalone host can report them (see <c>docs/SYSTEM_HEALTH.md</c> §2.3). Keys match the legacy
/// keys so both sources collapse to one row in the V2 hybrid host.
/// </summary>
public sealed class DatabaseStatusContributor(IDbContextFactory<SiNetSQLDbContext> factory)
    : ISubsystemStatusContributor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    public string Key => "database";

    public string DisplayNameHe => "מסד נתונים (SiNet SQL)";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var connected = await CanConnectAsync(_factory, cancellationToken).ConfigureAwait(false);

        return new SubsystemRuntimeStatus(
            Key,
            DisplayNameHe,
            connected ? SubsystemRuntimeState.Idle : SubsystemRuntimeState.Degraded,
            null,
            connected ? "מחובר" : "אין חיבור למסד הנתונים",
            DateTimeOffset.UtcNow);
    }

    internal static async Task<bool> CanConnectAsync(
        IDbContextFactory<SiNetSQLDbContext> factory,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        await using var db = await factory.CreateDbContextAsync(timeout.Token).ConfigureAwait(false);
        return await db.Database.CanConnectAsync(timeout.Token).ConfigureAwait(false);
    }
}

/// <summary>
/// Workflow engine availability. Legacy derived this row from the <c>database</c> row inside the
/// aggregator; contributors are independent by design, so this probes the same connection directly.
/// The reported signal is identical.
/// </summary>
public sealed class WorkflowEngineStatusContributor(IDbContextFactory<SiNetSQLDbContext> factory)
    : ISubsystemStatusContributor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    public string Key => "workflow";

    public string DisplayNameHe => "Workflow Engine";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var connected = await DatabaseStatusContributor
            .CanConnectAsync(_factory, cancellationToken)
            .ConfigureAwait(false);

        return new SubsystemRuntimeStatus(
            Key,
            DisplayNameHe,
            connected ? SubsystemRuntimeState.Idle : SubsystemRuntimeState.Degraded,
            null,
            connected ? "זמין" : "לא זמין — אין חיבור למסד הנתונים",
            DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Existence-only probe of a representative project root. Performs no writes and no traversal, and
/// runs the check on a worker thread under a bounded wait so a stale UNC path cannot hang the panel.
/// </summary>
public sealed class FileServerStatusContributor(IDbContextFactory<SiNetSQLDbContext> factory)
    : ISubsystemStatusContributor
{
    private static readonly TimeSpan ExistsTimeout = TimeSpan.FromSeconds(5);

    private readonly IDbContextFactory<SiNetSQLDbContext> _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    public string Key => "file-server";

    public string DisplayNameHe => "שרת קבצים (FileServer)";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ResolveProbePathAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(probe))
            return Row(SubsystemRuntimeState.NotConfigured, "אין נתיב פרויקט זמין לבדיקה");

        var existsTask = Task.Run(() =>
        {
            try
            {
                return Directory.Exists(probe);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }, cancellationToken);

        var completed = await Task.WhenAny(existsTask, Task.Delay(ExistsTimeout, cancellationToken))
            .ConfigureAwait(false);

        if (completed != existsTask)
            return Row(SubsystemRuntimeState.Degraded, $"תגובה איטית מנתיב הרשת — {probe}");

        return await existsTask.ConfigureAwait(false)
            ? Row(SubsystemRuntimeState.Idle, $"נגיש — {probe}")
            : Row(SubsystemRuntimeState.Degraded, $"הנתיב לא נמצא או לא נגיש — {probe}");
    }

    private async Task<string?> ResolveProbePathAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        await using var db = await _factory.CreateDbContextAsync(timeout.Token).ConfigureAwait(false);

        // The full path is computed from Place.Title + NameAndNumber, so the row must be hydrated
        // with its Place before the path can be built.
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Place)
            .Where(p => p.NameAndNumber != null && p.NameAndNumber != ""
                        && p.Place != null && p.Place.Title != null && p.Place.Title != "")
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(timeout.Token)
            .ConfigureAwait(false);

        var fullPath = FileServerRootResolver.BuildProjectFullPath(project);
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        try
        {
            return Directory.GetParent(fullPath)?.FullName ?? fullPath;
        }
        catch (IOException)
        {
            return fullPath;
        }
    }

    private SubsystemRuntimeStatus Row(SubsystemRuntimeState state, string summary) =>
        new(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
}

/// <summary>
/// Reachability of the Ollama endpoint plus presence of the configured model, matching the legacy
/// check: a reachable server that does not host the configured model is Degraded, not healthy.
/// </summary>
public sealed class OllamaStatusContributor(ISystemSettingsQueryService settings) : ISubsystemStatusContributor
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public string Key => "ollama";

    public string DisplayNameHe => "שרת AI (Ollama)";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var baseUrl = dto.Ai.OllamaBaseUrl;
        var model = dto.Ai.OllamaModel;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return Row(SubsystemRuntimeState.NotConfigured, "כתובת שרת AI לא הוגדרה");

        var tagsUrl = baseUrl.TrimEnd('/') + "/api/tags";

        try
        {
            using var response = await Http.GetAsync(tagsUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Row(SubsystemRuntimeState.Degraded, $"השרת החזיר {(int)response.StatusCode}");

            if (string.IsNullOrWhiteSpace(model))
                return Row(SubsystemRuntimeState.Idle, "השרת זמין — לא הוגדר מודל");

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return HasModel(body, model)
                ? Row(SubsystemRuntimeState.Idle, $"זמין — {model}")
                : Row(SubsystemRuntimeState.Degraded, $"השרת זמין אך המודל '{model}' לא נמצא");
        }
        catch (HttpRequestException ex)
        {
            return Row(SubsystemRuntimeState.Degraded, $"השרת אינו נגיש: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Row(SubsystemRuntimeState.Degraded, "תם הזמן בהמתנה לשרת AI");
        }
    }

    /// <summary>Ollama reports tags as <c>name:tag</c>, so a configured bare model name must match the prefix too.</summary>
    private static bool HasModel(string tagsJson, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(tagsJson);
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return false;

            foreach (var entry in models.EnumerateArray())
            {
                if (!entry.TryGetProperty("name", out var nameProperty))
                    continue;

                var name = nameProperty.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.Equals(name, model, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private SubsystemRuntimeStatus Row(SubsystemRuntimeState state, string summary) =>
        new(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
}

/// <summary>
/// Read-only seed baseline: required Codes from basic seed still present (see <c>docs/DEV_TOOLS.md</c>).
/// </summary>
public sealed class SeedBaselineStatusContributor(ISeedBaselineVerifyService verify)
    : ISubsystemStatusContributor
{
    public const string StatusKey = "seed-baseline";

    private readonly ISeedBaselineVerifyService _verify =
        verify ?? throw new ArgumentNullException(nameof(verify));

    public string Key => StatusKey;

    public string DisplayNameHe => "Seed בסיסי (Codes)";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        SeedBaselineVerifyResult result;
        try
        {
            result = await _verify.VerifyAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                SubsystemRuntimeState.Degraded,
                null,
                "בדיקת Seed — תם הזמן",
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                SubsystemRuntimeState.Degraded,
                null,
                "בדיקת Seed נכשלה: " + ex.Message,
                DateTimeOffset.UtcNow);
        }

        if (result.HasRequiredGaps)
        {
            return new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                SubsystemRuntimeState.Degraded,
                null,
                "חסרים Codes: " + result.FormatSummaryHe(),
                DateTimeOffset.UtcNow);
        }

        if (result.HasPrerequisiteWarnings)
        {
            return new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                SubsystemRuntimeState.Degraded,
                null,
                "אזהרה (תנאי catalog): " + result.FormatSummaryHe(),
                DateTimeOffset.UtcNow);
        }

        return new SubsystemRuntimeStatus(
            Key,
            DisplayNameHe,
            SubsystemRuntimeState.Idle,
            null,
            "Seed בסיסי שלם",
            DateTimeOffset.UtcNow);
    }
}
