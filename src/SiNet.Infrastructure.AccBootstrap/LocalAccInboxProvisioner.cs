using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNet.Application.Settings;
using SiNetSQL.Data;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// In-process implementation of <see cref="IAccInboxProvisioner"/>. Wraps the
/// existing <see cref="AccBootstrapService"/> directly — used in dev / single-machine
/// installs where the current Windows user IS the ACC Account Admin.
///
/// Reads Autodesk credentials from the injected <see cref="ITokenProvider"/> and inbox
/// configuration (project name, folder name, optional template) from
/// <see cref="ISystemSettingsQueryService"/>, matching the previous in-line behavior of
/// <c>EmailIngestionServiceFactory.TryRunBootstrapOnDemandAsync</c>.
/// </summary>
public sealed class LocalAccInboxProvisioner : IAccInboxProvisioner
{
    private readonly ITokenProvider _tokenProvider;
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory;
    private readonly ISystemSettingsQueryService _settingsService;

    public LocalAccInboxProvisioner(
        ITokenProvider tokenProvider,
        IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
        ISystemSettingsQueryService settingsService)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <inheritdoc />
    public async Task<(string AccProjectId, string AccInboxFolderId)> EnsureAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetSystemSettingsAsync(cancellationToken);
        var inboxProjectName = string.IsNullOrWhiteSpace(settings.EmailOffice.InboxProjectName)
            ? "מיילים למשרד - POC 4"
            : settings.EmailOffice.InboxProjectName!;
        var inboxFolderName = string.IsNullOrWhiteSpace(settings.EmailOffice.InboxFolderName)
            ? "_Inbox"
            : settings.EmailOffice.InboxFolderName;
        var templateName = settings.Acc.AccProjectTemplateName;

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var bim360 = new Bim360Service(_tokenProvider);

        var bootstrap = new AccBootstrapService(
            db, bim360,
            inboxProjectName: inboxProjectName,
            inboxFolderName: inboxFolderName,
            forceCreateProject: true,
            createPlatform: CreateProjectPlatform.AccNative,
            bootstrapAdminEmail: string.Empty,
            dryRun: false,
            templateName: templateName);

        var caller = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var targets = await bootstrap.EnsureOfficeInboxAsync(caller, cancellationToken);

        return (targets.AccProjectId, targets.AccInboxFolderId);
    }
}
