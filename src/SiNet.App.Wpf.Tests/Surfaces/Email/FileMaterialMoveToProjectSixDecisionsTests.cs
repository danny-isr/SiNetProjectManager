using System.IO;
using SiNet.Application.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

/// <summary>
/// Source / contract guards for FileMaterial MoveToProject six decisions (2026-08).
/// </summary>
public sealed class FileMaterialMoveToProjectSixDecisionsTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found from test base directory.");
    }

    [Fact]
    public void Executor_verifies_AlreadyMoved_target_instead_of_blind_failure()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailMoveToProjectExecutor.cs");
        Assert.Contains("MatchesCurrentMoveTarget", source, StringComparison.Ordinal);
        Assert.Contains("AlreadyMovedConflict", source, StringComparison.Ordinal);
        Assert.Contains("alreadySameSourceCount++", source, StringComparison.Ordinal);
        // Blind Always-fail on MoveMovedToProject must not remain as the only path.
        Assert.DoesNotContain(
            "RecordFailure(attachmentFailures, ref failedCount, att, \"AlreadyMovedToProject\");",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Executor_TotalCount_includes_required_without_requiring_AccItemId_upfront()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailMoveToProjectExecutor.cs");
        Assert.Contains("IsRequiredBusinessAttachment", source, StringComparison.Ordinal);
        Assert.Contains("TryReconcileAndRecoverAsync", source, StringComparison.Ordinal);
        Assert.Contains("!a.IsExternalDownload", source, StringComparison.Ordinal);
        Assert.Contains("MissingAccItemId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Executor_FiledButMoveMetadataFailed_is_process_failure_not_warning_only()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailMoveToProjectExecutor.cs");
        Assert.Contains("FiledButMoveMetadataFailed", source, StringComparison.Ordinal);
        Assert.Contains("INACTIVE (FileMaterial six decisions 2026-08): previous behavior only raised", source, StringComparison.Ordinal);
        Assert.DoesNotContain("warningCount +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("int warningCount = 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailVm_does_not_dismiss_on_AllFilesTransferred_alone()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        Assert.Contains("mayDismissFilingSurface", source, StringComparison.Ordinal);
        Assert.Contains("WorkflowAdvancePending", source, StringComparison.Ordinal);
        Assert.Contains("Do NOT dismiss — CompleteAsync failed", source, StringComparison.Ordinal);
        Assert.Contains("if (mayDismissFilingSurface && _workSurfaceContext?.TaskId is not null)", source, StringComparison.Ordinal);
        Assert.Contains("INACTIVE: dismiss on AllFilesTransferred alone", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Body_pdf_is_taggable_and_empty_attachments_policy_exists()
    {
        var tagging = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Detail/SqlEmailAttachmentTaggingService.cs");
        Assert.Contains("EmailBodyAttachmentIndex", tagging, StringComparison.Ordinal);
        Assert.Contains("EmailBodyFileName", tagging, StringComparison.Ordinal);

        var detail = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        Assert.Contains("TryResolveEmptyAttachmentsPolicyAsync", detail, StringComparison.Ordinal);
        Assert.Contains("תוכן המייל (PDF)", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_open_uses_direct_gmail_locate_not_page_only()
    {
        var window = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.Contains("TryLocateAndSelectTaskEmailAsync", window, StringComparison.Ordinal);

        var list = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        Assert.Contains("GetByIdAsync", list, StringComparison.Ordinal);
        Assert.Contains("BuildRfc822MessageIdSearchTerm", list, StringComparison.Ordinal);
        Assert.Contains("TryGetGmailApiMessageId", list, StringComparison.Ordinal);
        Assert.Contains("InjectAndSelectTaskRow", list, StringComparison.Ordinal);

        var composer = ReadRepoFile("src/SiNet.Application/Abstractions/Email/EmailMailboxQueryComposer.cs");
        Assert.Contains("rfc822msgid:", composer, StringComparison.Ordinal);

        var workItem = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWorkItemWindow.xaml");
        Assert.Contains("StatusMessage", workItem, StringComparison.Ordinal);

        var display = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListRowDisplayCoordinator.cs");
        Assert.Contains("never fall back to subject/from", display, StringComparison.Ordinal);
        Assert.Contains("Do not replace with the first row", display, StringComparison.Ordinal);
    }

    [Fact]
    public void Executor_zip_container_requires_folder_urn_not_tip_version()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailMoveToProjectExecutor.cs");
        Assert.Contains("IsAccFolderUrn", source, StringComparison.Ordinal);
        Assert.Contains(":fs.folder:", source, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(attachment.AccItemId)", source, StringComparison.Ordinal);
        // Tip version must not qualify as ZIP folder container.
        Assert.False(SiNet.Infrastructure.Sql.Services.Email.Acc.NativeEmailMoveToProjectExecutor.IsAccFolderUrn(
            "urn:adsk.wipprod:fs.file:vf.abc?version=1"));
        Assert.True(SiNet.Infrastructure.Sql.Services.Email.Acc.NativeEmailMoveToProjectExecutor.IsAccFolderUrn(
            "urn:adsk.wipprod:fs.folder:co.abc"));
    }

    [Fact]
    public void AllFilesTransferred_false_when_metadata_failure_counted()
    {
        var result = new EmailMoveToProjectCoordinatorResult(
            EmailMoveToProjectOutcome.Failed,
            "x",
            MovedCount: 0,
            FailedCount: 1,
            AttachmentFailures:
            [
                new EmailMoveToProjectAttachmentFailure(1, "a.pdf", "FiledButMoveMetadataFailed"),
            ],
            TotalCount: 1,
            AlreadySameSourceCount: 0);
        Assert.False(result.AllFilesTransferred);
    }

    [Fact]
    public void AllFilesTransferred_true_when_verified_same_source_fills_total()
    {
        var result = new EmailMoveToProjectCoordinatorResult(
            EmailMoveToProjectOutcome.Succeeded,
            "x",
            MovedCount: 0,
            FailedCount: 0,
            TotalCount: 2,
            AlreadySameSourceCount: 2);
        Assert.True(result.AllFilesTransferred);
    }
}
