using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Services.AccBootstrap;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Temporary host-side executor for local privileged ACC Inbox bootstrap.
/// This stays in the legacy host on purpose so <c>SiNet.App.Wpf</c> does not
/// reference legacy DB or connector runtime types.
/// </summary>
internal sealed class LegacyHostLocalAccInboxBootstrapExecutor(
    ITokenProvider tokenProvider,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ISystemSettingsQueryService systemSettingsQueryService) : IAccInboxBootstrapLocalExecutor
{
    private readonly ITokenProvider _tokenProvider = tokenProvider;
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory;
    private readonly ISystemSettingsQueryService _systemSettingsQueryService = systemSettingsQueryService;

    public async Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(_tokenProvider);

        var systemSettings = await _systemSettingsQueryService
            .GetSystemSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        var inboxProjectName = string.IsNullOrWhiteSpace(systemSettings.EmailOffice.InboxProjectName)
            ? "מיילים למשרד - POC 4"
            : systemSettings.EmailOffice.InboxProjectName.Trim();
        var inboxFolderName = string.IsNullOrWhiteSpace(systemSettings.EmailOffice.InboxFolderName)
            ? SystemSettingsDefaults.InboxFolderNameFallback
            : systemSettings.EmailOffice.InboxFolderName.Trim();
        var templateName = string.IsNullOrWhiteSpace(systemSettings.Acc.AccProjectTemplateName)
            ? null
            : systemSettings.Acc.AccProjectTemplateName.Trim();
        var bootstrapAdminEmail = string.IsNullOrWhiteSpace(systemSettings.Acc.AccBootstrapAdminEmail)
            ? string.Empty
            : systemSettings.Acc.AccBootstrapAdminEmail.Trim();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var bootstrap = new AccBootstrapService(
            db,
            new Bim360Service(_tokenProvider),
            inboxProjectName: inboxProjectName,
            inboxFolderName: inboxFolderName,
            forceCreateProject: true,
            createPlatform: CreateProjectPlatform.AccNative,
            bootstrapAdminEmail: bootstrapAdminEmail,
            dryRun: false,
            templateName: templateName);

        var caller = ResolveCurrentUser();
        var result = await bootstrap
            .EnsureOfficeInboxAsync(caller, cancellationToken)
            .ConfigureAwait(false);

        return new AccInboxBootstrapResult(
            result.HubId,
            result.AccProjectId,
            result.AccRootFolderId,
            result.AccInboxFolderId);
    }

    private static string ResolveCurrentUser()
    {
        try
        {
            return System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        }
        catch
        {
            return Environment.UserDomainName + "\\" + Environment.UserName;
        }
    }
}
