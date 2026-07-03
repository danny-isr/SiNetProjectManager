using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

internal enum AccControlPlaneStatusPresentationKind
{
    SecretSetup = 0,
    SettingsRuntime = 1,
    StatusWindow = 2,
}

internal sealed record AccControlPlaneStatusPresentation(
    string? Hint,
    string ModeSummary,
    string KeySummary,
    string ProjectsSummary,
    string HealthSummary,
    string DiagnosticsSummary);

public sealed class AccControlPlaneStatusPresenter(
    IAccServiceModeProvider accServiceModeProvider,
    IAccProjectService accProjectService,
    IAccServiceKeyDiagnostics accServiceKeyDiagnostics,
    IAccServiceHealthProbe accServiceHealthProbe,
    IAccServiceDiagnosticsProbe accServiceDiagnosticsProbe)
{
    private readonly IAccServiceModeProvider _accServiceModeProvider = accServiceModeProvider
        ?? throw new ArgumentNullException(nameof(accServiceModeProvider));
    private readonly IAccProjectService _accProjectService = accProjectService
        ?? throw new ArgumentNullException(nameof(accProjectService));
    private readonly IAccServiceKeyDiagnostics _accServiceKeyDiagnostics = accServiceKeyDiagnostics
        ?? throw new ArgumentNullException(nameof(accServiceKeyDiagnostics));
    private readonly IAccServiceHealthProbe _accServiceHealthProbe = accServiceHealthProbe
        ?? throw new ArgumentNullException(nameof(accServiceHealthProbe));
    private readonly IAccServiceDiagnosticsProbe _accServiceDiagnosticsProbe = accServiceDiagnosticsProbe
        ?? throw new ArgumentNullException(nameof(accServiceDiagnosticsProbe));

    internal async Task<AccControlPlaneStatusPresentation> BuildAsync(
        AccControlPlaneStatusPresentationKind kind,
        CancellationToken cancellationToken = default)
    {
        var labels = kind switch
        {
            AccControlPlaneStatusPresentationKind.SecretSetup => SecretSetupLabels,
            AccControlPlaneStatusPresentationKind.SettingsRuntime => SettingsRuntimeLabels,
            AccControlPlaneStatusPresentationKind.StatusWindow => StatusWindowLabels,
            _ => SecretSetupLabels,
        };

        var mode = _accServiceModeProvider.Mode;
        var baseUrl = _accServiceModeProvider.BaseUrl;
        var modeSummary = mode switch
        {
            AccServiceMode.Remote when !string.IsNullOrWhiteSpace(baseUrl) => $"{labels.ModeRemotePrefix} ({baseUrl})",
            _ => labels.ModeLocalText,
        };

        var keyInfo = _accServiceKeyDiagnostics.Describe();
        var keySummary = keyInfo.HasApiKey
            ? $"{labels.KeyPresentPrefix}, אורך {keyInfo.KeyLength}, hash {keyInfo.KeyHashPrefix}"
            : labels.KeyMissingText;
        var projectsSummary = await BuildProjectsSummaryAsync(labels, cancellationToken).ConfigureAwait(true);

        if (mode != AccServiceMode.Remote || string.IsNullOrWhiteSpace(baseUrl))
        {
            return new AccControlPlaneStatusPresentation(
                labels.Hint,
                modeSummary,
                keySummary,
                projectsSummary,
                labels.HealthLocalText,
                labels.DiagnosticsLocalText);
        }

        var health = await _accServiceHealthProbe.CheckAsync(cancellationToken).ConfigureAwait(true);
        var healthSummary = health.State switch
        {
            AccServiceHealthState.Online => $"{labels.HealthOnlinePrefix} ({health.Endpoint})",
            AccServiceHealthState.NotConfigured => labels.HealthNotConfiguredText,
            _ => $"{labels.HealthOfflinePrefix} ({health.Detail ?? "ללא פירוט"})",
        };

        var diagnostics = await _accServiceDiagnosticsProbe.ProbeAsync(cancellationToken).ConfigureAwait(true);
        var diagnosticsSummary = !diagnostics.Reachable
            ? $"{labels.DiagnosticsUnreachablePrefix}. Autodesk={diagnostics.AutodeskDetail ?? "ללא פירוט"}; DB={diagnostics.DbDetail ?? "ללא פירוט"}"
            : $"{labels.DiagnosticsReachablePrefix}: user={ResolveOrUnknown(diagnostics.WindowsUser)}; keySource={ResolveOrUnknown(diagnostics.KeySource)}; keyHash={ResolveOrNone(diagnostics.KeyHashPrefix)}; Autodesk={(diagnostics.AutodeskOk ? "ok" : "fail")}; DB={(diagnostics.DbOk ? "ok" : "fail")}";

        return new AccControlPlaneStatusPresentation(
            labels.Hint,
            modeSummary,
            keySummary,
            projectsSummary,
            healthSummary,
            diagnosticsSummary);
    }

    private async Task<string> BuildProjectsSummaryAsync(
        AccControlPlaneStatusLabels labels,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectIds = await _accProjectService.GetProjectIdsAsync(cancellationToken).ConfigureAwait(true);
            if (projectIds.Count == 0)
            {
                return labels.ProjectsEmptyText;
            }

            var preview = string.Join(", ", projectIds.Take(3));
            var extraCount = projectIds.Count - 3;
            var suffix = extraCount > 0 ? $" (+{extraCount} נוספים)" : string.Empty;
            return $"{labels.ProjectsPrefix}: {projectIds.Count} ({preview}{suffix})";
        }
        catch (Exception ex)
        {
            return $"{labels.ProjectsErrorPrefix}: {ex.Message}";
        }
    }

    private static string ResolveOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static string ResolveOrNone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    private static readonly AccControlPlaneStatusLabels SecretSetupLabels = new(
        Hint: null,
        ModeRemotePrefix: "מצב ACC: שירות מרכזי",
        ModeLocalText: "מצב ACC: מקומי (AccService:BaseUrl לא מוגדר)",
        KeyPresentPrefix: "מפתח ACC: קיים ב-Vault",
        KeyMissingText: "מפתח ACC: לא הוגדר ב-Vault.",
        ProjectsPrefix: "פרויקטי ACC מוכרים",
        ProjectsEmptyText: "פרויקטי ACC מוכרים: לא נמצאו מזהים ידועים.",
        ProjectsErrorPrefix: "פרויקטי ACC מוכרים: שגיאה בקריאה",
        HealthLocalText: "בריאות שירות ACC: לא רלוונטי במצב מקומי.",
        HealthOnlinePrefix: "בריאות שירות ACC: זמין",
        HealthNotConfiguredText: "בריאות שירות ACC: לא מוגדר.",
        HealthOfflinePrefix: "בריאות שירות ACC: לא זמין",
        DiagnosticsLocalText: "אבחון ACC: מצב מקומי, ללא קריאת /v1/acc/diag.",
        DiagnosticsUnreachablePrefix: "אבחון ACC: לא זמין",
        DiagnosticsReachablePrefix: "אבחון ACC");

    private static readonly AccControlPlaneStatusLabels SettingsRuntimeLabels = new(
        Hint: "מצב הריצה להלן משקף את ההוסט הנוכחי בלבד. שמירת Base URL כותבת ל-DB; restart נדרש כדי להחיל את הערך החדש.",
        ModeRemotePrefix: "מצב ריצה ACC: שירות מרכזי",
        ModeLocalText: "מצב ריצה ACC: מקומי (AccService:BaseUrl לא מוגדר בהוסט הנוכחי)",
        KeyPresentPrefix: "מפתח ריצה ACC: קיים ב-Vault",
        KeyMissingText: "מפתח ריצה ACC: לא הוגדר ב-Vault.",
        ProjectsPrefix: "פרויקטי ריצה ACC מוכרים",
        ProjectsEmptyText: "פרויקטי ריצה ACC מוכרים: לא נמצאו מזהים ידועים.",
        ProjectsErrorPrefix: "פרויקטי ריצה ACC מוכרים: שגיאה בקריאה",
        HealthLocalText: "בריאות ריצה ACC: לא רלוונטי במצב מקומי.",
        HealthOnlinePrefix: "בריאות ריצה ACC: זמין",
        HealthNotConfiguredText: "בריאות ריצה ACC: לא מוגדר.",
        HealthOfflinePrefix: "בריאות ריצה ACC: לא זמין",
        DiagnosticsLocalText: "אבחון ריצה ACC: מצב מקומי, ללא קריאת /v1/acc/diag.",
        DiagnosticsUnreachablePrefix: "אבחון ריצה ACC: לא זמין",
        DiagnosticsReachablePrefix: "אבחון ריצה ACC");

    private static readonly AccControlPlaneStatusLabels StatusWindowLabels = new(
        Hint: "חלון זה מציג את מצב הריצה הנוכחי של ההוסט בלבד. אין כאן שינוי הגדרות או כתיבה ל-ACC.",
        ModeRemotePrefix: "מצב ריצה ACC: שירות מרכזי",
        ModeLocalText: "מצב ריצה ACC: מקומי (AccService:BaseUrl לא מוגדר בהוסט הנוכחי)",
        KeyPresentPrefix: "מפתח ריצה ACC: קיים ב-Vault",
        KeyMissingText: "מפתח ריצה ACC: לא הוגדר ב-Vault.",
        ProjectsPrefix: "פרויקטי ריצה ACC מוכרים",
        ProjectsEmptyText: "פרויקטי ריצה ACC מוכרים: לא נמצאו מזהים ידועים.",
        ProjectsErrorPrefix: "פרויקטי ריצה ACC מוכרים: שגיאה בקריאה",
        HealthLocalText: "בריאות ריצה ACC: לא רלוונטי במצב מקומי.",
        HealthOnlinePrefix: "בריאות ריצה ACC: זמין",
        HealthNotConfiguredText: "בריאות ריצה ACC: לא מוגדר.",
        HealthOfflinePrefix: "בריאות ריצה ACC: לא זמין",
        DiagnosticsLocalText: "אבחון ריצה ACC: מצב מקומי, ללא קריאת /v1/acc/diag.",
        DiagnosticsUnreachablePrefix: "אבחון ריצה ACC: לא זמין",
        DiagnosticsReachablePrefix: "אבחון ריצה ACC");
}

internal sealed record AccControlPlaneStatusLabels(
    string? Hint,
    string ModeRemotePrefix,
    string ModeLocalText,
    string KeyPresentPrefix,
    string KeyMissingText,
    string ProjectsPrefix,
    string ProjectsEmptyText,
    string ProjectsErrorPrefix,
    string HealthLocalText,
    string HealthOnlinePrefix,
    string HealthNotConfiguredText,
    string HealthOfflinePrefix,
    string DiagnosticsLocalText,
    string DiagnosticsUnreachablePrefix,
    string DiagnosticsReachablePrefix);
