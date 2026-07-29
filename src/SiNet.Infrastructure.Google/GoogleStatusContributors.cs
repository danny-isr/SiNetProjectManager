using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Configuration;
using SiNet.Application.Google;
using SiNet.Application.Runtime;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// «מצב מערכת» rows for the Google stack, ported from the legacy checks in
/// <c>SiNetProjectManagerV2\Services\Health\GoogleDiagnosticsHealthChecks.cs</c>.
/// Keys match the legacy keys so the two sources collapse to one row in the V2 hybrid host.
/// </summary>
public sealed class GoogleConfigStatusContributor(IGoogleClientSecretsPathProvider pathProvider)
    : ISubsystemStatusContributor
{
    private readonly IGoogleClientSecretsPathProvider _pathProvider =
        pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));

    public string Key => "google_config";

    public string DisplayNameHe => "חיבור Google (הגדרות)";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var path = await _pathProvider.ResolveClientSecretsPathAsync(cancellationToken).ConfigureAwait(false);

        var (state, summary) = string.IsNullOrWhiteSpace(path)
            ? (SubsystemRuntimeState.NotConfigured, "נתיב client secrets לא הוגדר")
            : File.Exists(path)
                ? (SubsystemRuntimeState.Idle, "קובץ client secrets נמצא")
                : (SubsystemRuntimeState.Degraded, $"קובץ client secrets חסר: {path}");

        return new SubsystemRuntimeStatus(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
    }
}

/// <summary>Reports which Google account the host is signed in as.</summary>
public sealed class GoogleAccountStatusContributor(IConnectorAuthService auth) : ISubsystemStatusContributor
{
    private readonly IConnectorAuthService _auth = auth ?? throw new ArgumentNullException(nameof(auth));

    public string Key => "google_account";

    public string DisplayNameHe => "חשבון Google מחובר";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        if (!_auth.IsAuthenticated)
        {
            // Silent restore only — a status probe must never open a browser.
            await _auth.TryRestoreSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!_auth.IsAuthenticated)
        {
            return new SubsystemRuntimeStatus(
                Key, DisplayNameHe, SubsystemRuntimeState.Stopped, null, "לא מחובר", DateTimeOffset.UtcNow);
        }

        var email = _auth.ConnectedAccountEmail;
        var summary = string.IsNullOrWhiteSpace(email) ? "מחובר" : $"מחובר — {email}";
        return new SubsystemRuntimeStatus(
            Key, DisplayNameHe, SubsystemRuntimeState.Idle, null, summary, DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Active Gmail reachability. Unlike the passive <c>gmail</c> row, this one actually calls Gmail —
/// a single-item mailbox page, which is the cheapest real round-trip available on the gateway.
/// </summary>
public sealed class GmailReachabilityStatusContributor(IConnectorAuthService auth, IEmailGateway gateway)
    : ISubsystemStatusContributor
{
    private readonly IConnectorAuthService _auth = auth ?? throw new ArgumentNullException(nameof(auth));
    private readonly IEmailGateway _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    public string Key => "google";

    public string DisplayNameHe => "Google / Gmail";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        if (!_auth.IsAuthenticated)
        {
            return new SubsystemRuntimeStatus(
                Key, DisplayNameHe, SubsystemRuntimeState.Stopped, null, "לא מחובר — נדרשת הרשאה", DateTimeOffset.UtcNow);
        }

        var page = await _gateway
            .GetMailboxPageAsync(new EmailMailboxQuery { PageSize = 1 }, pageToken: null, cancellationToken)
            .ConfigureAwait(false);

        // The gateway returns an empty page instead of throwing when the mailbox is unavailable, so an
        // empty page on an authenticated session is treated as "reachable but nothing to show".
        var summary = page.Items.Count > 0 ? "Gmail נגיש" : "Gmail נגיש — אין הודעות בעמוד";
        return new SubsystemRuntimeStatus(
            Key, DisplayNameHe, SubsystemRuntimeState.Idle, null, summary, DateTimeOffset.UtcNow);
    }
}

/// <summary>Shared shape for the two configured Drive folders (templates and reports).</summary>
public abstract class GoogleDriveFolderStatusContributorBase(
    ISystemSettingsQueryService settings,
    IGoogleDriveFolderDiagnostics diagnostics) : ISubsystemStatusContributor
{
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IGoogleDriveFolderDiagnostics _diagnostics =
        diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public abstract string Key { get; }

    public abstract string DisplayNameHe { get; }

    /// <summary>Templates must contain at least one spreadsheet.</summary>
    protected abstract bool ExpectSpreadsheets { get; }

    /// <summary>Inspection reports folder must allow creating children (write).</summary>
    protected abstract bool RequireWriteAccess { get; }

    protected abstract string ResolveFolderId(InspectionSystemSettingsDto inspection);

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folderId = ResolveFolderId(dto.Inspection);

        var result = await _diagnostics
            .DiagnoseAsync(folderId, ExpectSpreadsheets, RequireWriteAccess, cancellationToken)
            .ConfigureAwait(false);

        var (state, summary) = Describe(result);
        return new SubsystemRuntimeStatus(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
    }

    public static (SubsystemRuntimeState State, string Summary) Describe(GoogleDriveFolderDiagnosticResult r)
    {
        var name = string.IsNullOrWhiteSpace(r.FolderName) ? r.FolderIdSnippet : r.FolderName;

        return r.Status switch
        {
            GoogleDriveFolderStatus.Ok =>
                (SubsystemRuntimeState.Idle, $"תקין — {name}"),
            GoogleDriveFolderStatus.ReadOnlyOrUnknownWrite =>
                (SubsystemRuntimeState.Idle, $"נגישה — {name}"),
            GoogleDriveFolderStatus.EmptyFolder =>
                (SubsystemRuntimeState.Degraded, $"התיקייה ריקה — {name}"),
            GoogleDriveFolderStatus.NotConfigured =>
                (SubsystemRuntimeState.NotConfigured, "מזהה התיקייה לא הוגדר"),
            GoogleDriveFolderStatus.NotAuthenticated =>
                (SubsystemRuntimeState.Stopped, "אין חשבון Google מחובר"),
            GoogleDriveFolderStatus.NoAccess =>
                (SubsystemRuntimeState.Degraded, "לחשבון Google המחובר אין הרשאה לגשת אליה"),
            GoogleDriveFolderStatus.NoWriteAccess =>
                (SubsystemRuntimeState.Degraded, $"אין הרשאת כתיבה — {name}"),
            GoogleDriveFolderStatus.NotFound =>
                (SubsystemRuntimeState.Degraded, "התיקייה לא נמצאה"),
            GoogleDriveFolderStatus.InvalidType =>
                (SubsystemRuntimeState.Degraded, "המזהה אינו תיקייה"),
            _ =>
                (SubsystemRuntimeState.Degraded, $"שגיאה: {r.TechnicalDetails}"),
        };
    }
}

public sealed class GoogleTemplatesFolderStatusContributor(
    ISystemSettingsQueryService settings,
    IGoogleDriveFolderDiagnostics diagnostics)
    : GoogleDriveFolderStatusContributorBase(settings, diagnostics)
{
    public override string Key => SystemSettingKeys.InspectionTemplatesFolderId;

    public override string DisplayNameHe => "תיקיית תבניות בדרייב";

    protected override bool ExpectSpreadsheets => true;

    protected override bool RequireWriteAccess => false;

    protected override string ResolveFolderId(InspectionSystemSettingsDto inspection) =>
        inspection.InspectionTemplatesFolderId;
}

public sealed class GoogleReportsFolderStatusContributor(
    ISystemSettingsQueryService settings,
    IGoogleDriveFolderDiagnostics diagnostics)
    : GoogleDriveFolderStatusContributorBase(settings, diagnostics)
{
    public override string Key => SystemSettingKeys.InspectionReportsFolderId;

    public override string DisplayNameHe => "תיקיית דוחות בדרייב";

    protected override bool ExpectSpreadsheets => false;

    protected override bool RequireWriteAccess => true;

    protected override string ResolveFolderId(InspectionSystemSettingsDto inspection) =>
        inspection.InspectionReportsFolderId;
}

/// <summary>
/// MasterPlan R01/R02/R03 Shared Drive write access — the exact probe report generation runs before
/// creating sheets. Distinct from <see cref="GoogleReportsFolderStatusContributor"/> which covers
/// the Inspection reports folder id.
/// </summary>
public sealed class MasterPlanReportsDriveStatusContributor(
    GmailOptions options,
    IGoogleDriveFolderDiagnostics diagnostics) : ISubsystemStatusContributor
{
    public const string StatusKey = "masterplan-reports-drive";

    private readonly GmailOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IGoogleDriveFolderDiagnostics _diagnostics =
        diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public string Key => StatusKey;

    public string DisplayNameHe => "Shared Drive לדוחות MasterPlan";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsReportsConfigured)
        {
            return new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                SubsystemRuntimeState.NotConfigured,
                null,
                "GoogleReports לא מוגדר (SharedDriveId / RootReportsFolderId)",
                DateTimeOffset.UtcNow);
        }

        var driveResult = await _diagnostics
            .DiagnoseSharedDriveWriteAsync(_options.EffectiveReportsSharedDriveId, cancellationToken)
            .ConfigureAwait(false);

        if (driveResult.Status != GoogleDriveFolderStatus.Ok)
        {
            var (driveState, driveSummary) = driveResult.Status switch
            {
                GoogleDriveFolderStatus.NoWriteAccess =>
                    (SubsystemRuntimeState.Degraded, "אין הרשאות כתיבה ל-Shared Drive"),
                GoogleDriveFolderStatus.NotAuthenticated =>
                    (SubsystemRuntimeState.Stopped, "אין חשבון Google מחובר"),
                GoogleDriveFolderStatus.NotConfigured =>
                    (SubsystemRuntimeState.NotConfigured, "מזהה Shared Drive לא הוגדר"),
                GoogleDriveFolderStatus.NoAccess or GoogleDriveFolderStatus.NotFound =>
                    (SubsystemRuntimeState.Degraded, "אין גישה ל-Shared Drive"),
                _ =>
                    (SubsystemRuntimeState.Degraded, $"שגיאה: {driveResult.TechnicalDetails}"),
            };
            return new SubsystemRuntimeStatus(Key, DisplayNameHe, driveState, null, driveSummary, DateTimeOffset.UtcNow);
        }

        // Shared Drive write ≠ root-folder write. Generation creates under ReportsRootFolderId.
        var rootResult = await _diagnostics
            .DiagnoseAsync(
                _options.ReportsRootFolderId,
                expectSpreadsheets: false,
                requireWriteAccess: true,
                cancellationToken)
            .ConfigureAwait(false);

        var (state, summary) = rootResult.Status switch
        {
            GoogleDriveFolderStatus.Ok =>
                (SubsystemRuntimeState.Idle,
                    string.IsNullOrWhiteSpace(driveResult.FolderName)
                        ? "יש הרשאת כתיבה ל-Shared Drive ולתיקיית הדוחות"
                        : $"יש הרשאת כתיבה — {driveResult.FolderName}"),
            GoogleDriveFolderStatus.NoWriteAccess =>
                (SubsystemRuntimeState.Degraded, "אין הרשאת כתיבה לתיקיית שורש הדוחות (RootReportsFolderId)"),
            GoogleDriveFolderStatus.NotAuthenticated =>
                (SubsystemRuntimeState.Stopped, "אין חשבון Google מחובר"),
            GoogleDriveFolderStatus.NotConfigured =>
                (SubsystemRuntimeState.NotConfigured, "RootReportsFolderId לא הוגדר"),
            GoogleDriveFolderStatus.NoAccess or GoogleDriveFolderStatus.NotFound =>
                (SubsystemRuntimeState.Degraded, "אין גישה לתיקיית שורש הדוחות"),
            _ =>
                (SubsystemRuntimeState.Degraded, $"שגיאה בתיקיית שורש הדוחות: {rootResult.TechnicalDetails}"),
        };

        return new SubsystemRuntimeStatus(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
    }
}
