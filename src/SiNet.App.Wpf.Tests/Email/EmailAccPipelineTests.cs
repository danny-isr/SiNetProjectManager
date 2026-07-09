using System.IO;
using Moq;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailAccPipelineTests
{
    [Fact]
    public void Email_acc_identity_uses_rfc822_message_id_not_gmail_id()
    {
        const string rfc822 = "<abc@example.com>";
        const string gmailId = "gmail-local-123";

        var uniqueId = EmailMessageIdentity.GetMessageUniqueId(rfc822, gmailId);

        Assert.Equal("abc@example.com", uniqueId);
        Assert.DoesNotContain("gmail:", uniqueId, StringComparison.Ordinal);
    }

    [Fact]
    public void No_gmail_message_id_business_identity_when_rfc822_present()
    {
        var uniqueId = EmailAccStatusMapper.ResolveMessageUniqueId("<msg@test.com>", "local-id");
        Assert.Equal("msg@test.com", uniqueId);
    }

    [Fact]
    public void Email_acc_upload_acquires_lock_before_upload_is_documented_in_legacy_lease()
    {
        Assert.Equal(15, EmailAccLeasePolicy.LeaseTtlMinutes);
    }

    [Fact]
    public void Expired_lock_can_be_recovered_safely_when_lease_is_stale()
    {
        var stale = DateTime.UtcNow.AddMinutes(-(EmailAccLeasePolicy.LeaseTtlMinutes + 1));
        Assert.True(EmailAccStatusMapper.IsStaleLease(stale));
    }

    [Fact]
    public void Second_user_cannot_upload_same_email_while_locked_maps_to_locked_status()
    {
        var cache = new EmailInboxAccCacheRow(
            1,
            "msg@test.com",
            EmailInboxStatus.Processing,
            "DOMAIN\\other",
            DateTime.UtcNow,
            null,
            2);

        var lockStatus = new EmailAccLockStatus(true, false, "DOMAIN\\other", DateTime.UtcNow, false);
        var status = EmailAccStatusMapper.Map("msg@test.com", cache, null, "DOMAIN\\me");

        Assert.Equal(EmailAccProcessingStatus.LockedByOtherUser, status.ProcessingStatus);
        Assert.True(status.IsLockedByOtherUser);
        Assert.False(lockStatus.IsHeldByCurrentUser);
    }

    [Fact]
    public void Db_cache_not_used_as_physical_existence_proof_when_reconciliation_missing()
    {
        var cache = new EmailInboxAccCacheRow(
            1,
            "msg@test.com",
            EmailInboxStatus.Uploaded,
            null,
            null,
            "folder-id",
            1);

        var status = EmailAccStatusMapper.Map("msg@test.com", cache, reconciliation: null, currentUserLogin: null);

        Assert.Equal(EmailAccProcessingStatus.ReconciliationRequired, status.ProcessingStatus);
    }

    [Fact]
    public void Acc_reconciliation_detects_missing_file()
    {
        var reconciliation = new AccInboxReconciliationResult(
            1,
            "proj",
            "folder",
            [
                new AccInboxAttachmentReconciliationItem(
                    10,
                    0,
                    "quote.pdf",
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    AccInboxAttachmentPresenceStatus.MissingInAcc,
                    "MissingInAcc",
                    null,
                    null,
                    false,
                    false,
                    false,
                    new Dictionary<string, string?>()),
            ]);

        var cache = new EmailInboxAccCacheRow(1, "msg@test.com", EmailInboxStatus.Uploaded, null, null, "folder", 1);
        var status = EmailAccStatusMapper.Map("msg@test.com", cache, reconciliation, null);

        Assert.Equal(EmailAccProcessingStatus.MissingInAcc, status.ProcessingStatus);
        Assert.Equal(1, status.MissingInAccCount);
    }

    [Fact]
    public void Upload_is_idempotent_when_already_processed()
    {
        var result = new EmailAccUploadResult(
            EmailAccUploadOutcome.AlreadyProcessed,
            "msg@test.com",
            1,
            2,
            2,
            null,
            0);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Email_list_shows_partial_failure_when_some_attachments_missing()
    {
        var reconciliation = new AccInboxReconciliationResult(
            1,
            "proj",
            "folder",
            [
                new AccInboxAttachmentReconciliationItem(
                    1, 0, "a.pdf", "id1", null, null, null, null, true,
                    AccInboxAttachmentPresenceStatus.ExistsInAcc, "OK", null, null, false, false, false, new Dictionary<string, string?>()),
                new AccInboxAttachmentReconciliationItem(
                    2, 1, "b.pdf", null, null, null, null, null, false,
                    AccInboxAttachmentPresenceStatus.MissingInAcc, "Missing", null, null, false, false, false, new Dictionary<string, string?>()),
            ]);

        var cache = new EmailInboxAccCacheRow(1, "msg@test.com", EmailInboxStatus.Uploaded, null, null, "folder", 2);
        var status = EmailAccStatusMapper.Map("msg@test.com", cache, reconciliation, null);

        Assert.Equal(EmailAccProcessingStatus.PartiallyUploaded, status.ProcessingStatus);
        Assert.True(status.HasPartialFailure);
    }

    [Fact]
    public void Reconciliation_without_db_row_maps_acc_truth_not_not_in_db()
    {
        var reconciliation = new AccInboxReconciliationResult(
            5,
            "proj",
            "folder",
            [
                new AccInboxAttachmentReconciliationItem(
                    1, 0, "a.pdf", "id1", null, null, null, null, true,
                    AccInboxAttachmentPresenceStatus.ExistsInAcc, "OK", null, null, false, false, false, new Dictionary<string, string?>()),
            ]);

        var status = EmailAccStatusMapper.Map("msg@test.com", cache: null, reconciliation, currentUserLogin: null);

        Assert.Equal(EmailAccProcessingStatus.UploadedToAcc, status.ProcessingStatus);
        Assert.Equal("הועלה ל-ACC Inbox", status.StatusDisplay);
        Assert.DoesNotContain("לא נמצא ב-DB", status.StatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_selection_triggers_passive_acc_ingest_after_details_load()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var handlerSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailAccSelectionHandler.cs");
        var selectionHandlerSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailSelectionCoordinator.cs");
        var detailVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");

        Assert.Contains("RunSelectionPipelineAsync", detailVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadSelectedEmailWithAccPipelineAsync", selectionHandlerSource, StringComparison.Ordinal);
        Assert.Contains("LoadBodyIfNeededAsync", selectionHandlerSource, StringComparison.Ordinal);
        Assert.Contains("TryPassiveAccIngestOnSelectionAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("TryPassiveIngestAsync", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("read-only, no upload", listVmSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Re_select_with_loaded_body_still_runs_acc_pipeline()
    {
        var detailVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        Assert.Contains("RunSelectionPipelineAsync", detailVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadSelectedEmailWithAccPipelineAsync", ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailSelectionCoordinator.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("HasLoadedBodyForCurrentSelection(value.Id))\n        {\n            return;", detailVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchRowAttachmentCount_updates_has_attachments_before_passive_ingest()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var selectionHandlerSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailSelectionCoordinator.cs");

        Assert.Contains("PatchRowAttachmentCount", listVmSource, StringComparison.Ordinal);
        Assert.Contains("PatchRowAttachmentCount(messageId, details.Attachments.Count)", selectionHandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Upload_outcome_display_maps_backend_not_available_to_hebrew()
    {
        var result = EmailAccUploadResult.BackendNotAvailable("msg@test.com");
        var text = EmailAccUploadOutcomeDisplay.ResolveFailureMessage(result);
        Assert.Contains("host", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ACC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Upload_outcome_display_maps_skipped_not_relevant_to_hebrew()
    {
        var result = new EmailAccUploadResult(
            EmailAccUploadOutcome.SkippedNotRelevant,
            "msg@test.com",
            null,
            0,
            0,
            null,
            0);

        var text = EmailAccUploadOutcomeDisplay.ResolveFailureMessage(result);
        Assert.Contains("לא רלוונטי", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_in_database_maps_to_not_uploaded_hebrew()
    {
        var status = EmailAccStatusMapper.Map("msg@test.com", cache: null, reconciliation: null, currentUserLogin: null);
        Assert.Equal(EmailAccProcessingStatus.NotInDatabase, status.ProcessingStatus);
        Assert.Equal("לא הועלה ל-ACC", status.StatusDisplay);
    }

    [Fact]
    public void External_download_link_detector_finds_jumbomail_and_wetransfer()
    {
        const string body = "Download from https://www.jumbomail.me/abc and https://we.tl/xyz";
        var urls = EmailExternalDownloadLinkDetector.ExtractUrls(body);
        Assert.Equal(2, urls.Count);
        Assert.True(EmailExternalDownloadLinkDetector.HasExternalDownloadLink(body));
    }

    [Fact]
    public void AddSiNetEmailAccSql_registers_external_download_coordinator()
    {
        var extensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailAccServiceCollectionExtensions.cs");
        Assert.Contains("IEmailExternalDownloadCoordinator", extensions, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncStatusWithRecovery_declared_on_acc_status_service()
    {
        var source = ReadRepoFile("src/SiNet.Application/Email/Acc/IEmailAccStatusService.cs");
        Assert.Contains("SyncStatusWithRecoveryAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailAccSelectionHandler_extracted_from_list_view_model()
    {
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "src/SiNet.App.Wpf/Surfaces/Email/EmailAccSelectionHandler.cs")));
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        Assert.Contains("EmailAccSelectionHandler", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAccRowActionAsync", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSiNetEmailAccSql_registered_in_composition()
    {
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");
        Assert.Contains("AddSiNetEmailAccSql", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void No_ui_direct_autodesk_connector_business_call_in_email_window_vm()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.DoesNotContain("AutodeskConnector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Bim360Service", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_LegacyBridge_as_target_architecture_in_acc_status_service()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Acc/SqlEmailAccStatusService.cs");
        Assert.DoesNotContain("LegacyBridge", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveToProject_coordinator_uses_executor_not_parallel_handler_in_vm()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.Contains("IEmailMoveToProjectService", source, StringComparison.Ordinal);
        Assert.Contains("EmailDetailViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveToProjectProcessActionHandler", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_upload_allows_retry_when_not_fully_complete()
    {
        var status = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.PartiallyUploaded,
            null,
            "חלקי",
            null,
            TotalAttachments: 2,
            ExistingInAccCount: 1,
            MissingInAccCount: 1,
            Attachments: []);

        Assert.False(EmailAccIngestGates.IsIngestFullyComplete(status));
        Assert.False(EmailAccIngestGates.ShouldSkipRetryAfterAttempt(status));
    }

    [Fact]
    public void Full_upload_blocks_retry_after_attempt()
    {
        var status = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.UploadedToAcc,
            null,
            "הועלה",
            "folder",
            TotalAttachments: 2,
            ExistingInAccCount: 2,
            MissingInAccCount: 0,
            Attachments: []);

        Assert.True(EmailAccIngestGates.IsIngestFullyComplete(status));
        Assert.True(EmailAccIngestGates.ShouldSkipRetryAfterAttempt(status));
    }

    [Fact]
    public void Legacy_ingest_executor_ensures_auth_before_load_full_email_body()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/Services/LegacyEmailAccIngestionExecutor.cs");
        Assert.Contains("EnsureAuthenticatedForAccIngestAsync", source, StringComparison.Ordinal);
        var ensureIndex = source.IndexOf("EnsureAuthenticatedForAccIngestAsync", StringComparison.Ordinal);
        var loadIndex = source.IndexOf("LoadFullEmailBodyAsync", StringComparison.Ordinal);
        Assert.True(ensureIndex >= 0);
        Assert.True(loadIndex > ensureIndex);
    }

    [Fact]
    public void Background_work_tracker_increments_and_decrements_in_scope()
    {
        var tracker = new EmailAccBackgroundWorkTracker();
        Assert.Equal(0, tracker.ActiveCount);

        using (tracker.BeginWork())
        {
            Assert.Equal(1, tracker.ActiveCount);
        }

        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact]
    public void Processing_with_all_attachments_in_acc_maps_to_uploaded()
    {
        var reconciliation = new AccInboxReconciliationResult(
            1,
            "proj",
            "folder",
            [
                new AccInboxAttachmentReconciliationItem(
                    1, 0, "a.pdf", "id1", null, null, null, null, true,
                    AccInboxAttachmentPresenceStatus.ExistsInAcc, "OK", null, null, false, false, false, new Dictionary<string, string?>()),
            ]);

        var cache = new EmailInboxAccCacheRow(
            1,
            "msg@test.com",
            EmailInboxStatus.Processing,
            "DOMAIN\\me",
            DateTime.UtcNow,
            "folder",
            1);

        var status = EmailAccStatusMapper.Map("msg@test.com", cache, reconciliation, "DOMAIN\\me");

        Assert.Equal(EmailAccProcessingStatus.UploadedToAcc, status.ProcessingStatus);
        Assert.Equal("הועלה ל-ACC Inbox", status.StatusDisplay);
    }

    [Fact]
    public void ResolveFinalAccStatus_prefers_upload_success_over_stuck_processing()
    {
        var upload = new EmailAccUploadResult(
            EmailAccUploadOutcome.Succeeded,
            "msg@test.com",
            1,
            2,
            2,
            null,
            100);

        var stuckSync = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.UploadInProgress,
            null,
            "העלאה ל-ACC מתבצעת…",
            null,
            2,
            0,
            0,
            []);

        var resolved = EmailAccUploadCompletionResolver.ResolveFinalAccStatus(upload, waitStatus: null, stuckSync);

        Assert.NotNull(resolved);
        Assert.Equal(EmailAccProcessingStatus.UploadedToAcc, resolved!.ProcessingStatus);
        Assert.Contains("הועלו 2/2", resolved.StatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Passive_ingest_clears_busy_when_selection_changes()
    {
        var pendingSync = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.PendingUpload,
            null,
            "ממתין להעלאה ל-ACC",
            null,
            2,
            0,
            0,
            []);

        var stuckSync = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.UploadInProgress,
            null,
            "העלאה ל-ACC מתבצעת…",
            null,
            2,
            0,
            0,
            []);

        var statusService = new Mock<IEmailAccStatusService>();
        statusService
            .SetupSequence(s => s.SyncStatusWithRecoveryAsync(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingSync)
            .ReturnsAsync(stuckSync);

        var uploadCoordinator = new Mock<IEmailAccUploadCoordinator>();
        uploadCoordinator
            .Setup(c => c.UploadToAccInboxAsync(It.IsAny<EmailAccUploadCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailAccUploadResult(
                EmailAccUploadOutcome.Succeeded,
                "msg@test.com",
                1,
                2,
                2,
                null,
                50));

        EmailListRow? patchedRow = null;
        var row = new EmailListRow(
            "gmail-1",
            "sender@test.com",
            "Subject",
            "Preview",
            DateTime.UtcNow,
            "Inbox",
            false,
            false,
            null,
            AttachmentCount: 2,
            InternetMessageId: "<msg@test.com>");

        var handler = new EmailAccSelectionHandler(
            statusService.Object,
            uploadCoordinator.Object,
            patch => patchedRow = patch,
            findRow: _ => patchedRow ?? row);

        var stillSelected = true;
        await handler.TryPassiveIngestAsync(row, () => stillSelected);

        Assert.NotNull(patchedRow);
        Assert.False(patchedRow!.IsAccUploadBusy);
        Assert.Equal(EmailAccProcessingStatus.UploadedToAcc, patchedRow.AccProcessingStatus);
        Assert.Contains("הועלו 2/2", patchedRow.AccStatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Locked_by_other_user_does_not_call_upload_coordinator()
    {
        var lockedStatus = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.LockedByOtherUser,
            new EmailAccLockStatus(true, false, "DOMAIN\\other", DateTime.UtcNow, false),
            "בטיפול על ידי משתמש אחר",
            null,
            TotalAttachments: 2,
            ExistingInAccCount: 0,
            MissingInAccCount: 2,
            Attachments: []);

        var statusService = new Mock<IEmailAccStatusService>();
        statusService
            .Setup(s => s.SyncStatusWithRecoveryAsync(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedStatus);

        var uploadCoordinator = new Mock<IEmailAccUploadCoordinator>();

        var handler = new EmailAccSelectionHandler(
            statusService.Object,
            uploadCoordinator.Object);

        var row = new EmailListRow(
            "gmail-1",
            "sender@test.com",
            "Subject",
            "Preview",
            DateTime.UtcNow,
            "Inbox",
            false,
            false,
            null,
            AttachmentCount: 2,
            InternetMessageId: "<msg@test.com>");

        await handler.TryPassiveIngestAsync(row, () => true);

        uploadCoordinator.Verify(
            c => c.UploadToAccInboxAsync(It.IsAny<EmailAccUploadCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Ingest_queue_registers_with_max_concurrency_five()
    {
        var extensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailAccServiceCollectionExtensions.cs");
        Assert.Contains("IEmailAccIngestQueue", extensions, StringComparison.Ordinal);
        Assert.Contains("DefaultMaxConcurrency = 5", ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Acc/EmailAccIngestQueue.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_close_uses_background_work_prompt()
    {
        var viewSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml.cs");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        Assert.Contains("TryBlockCloseForBackgroundWork", viewSource, StringComparison.Ordinal);
        Assert.Contains("IEmailAccClosePrompt", vmSource, StringComparison.Ordinal);
        Assert.Contains("Closing=\"EmailWindowView_OnClosing\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_failure_message_maps_not_logged_in_to_hebrew()
    {
        var mapped = EmailAccIngestGates.MapAuthFailureMessage("Not logged in.");
        Assert.Equal("Gmail לא מחובר להעלאה — לחץ התחבר מחדש", mapped);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = FindRepoRoot();
        return File.ReadAllText(Path.Combine(dir, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "SiNetProjectManager_GitHub", "src")))
            {
                return Path.Combine(dir, "SiNetProjectManager_GitHub");
            }

            if (Directory.Exists(Path.Combine(dir, "src", "SiNet.Application")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
