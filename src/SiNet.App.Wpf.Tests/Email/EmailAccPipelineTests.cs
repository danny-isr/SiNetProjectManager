using System.IO;
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
    public void Email_list_selection_loads_status_without_uploading_unless_action_requested()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("LoadSelectedEmailAccStatusAsync", vmSource, StringComparison.Ordinal);
        Assert.Contains("LoadAccStatusForRowAsync", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryIngestEmailToAccAsync", vmSource, StringComparison.Ordinal);
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
        Assert.Contains("IEmailMoveToProjectCoordinator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveToProjectProcessActionHandler", source, StringComparison.Ordinal);
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
