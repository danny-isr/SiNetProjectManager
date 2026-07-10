using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Services.Email.Detail;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailMoveToProjectEligibilityRulesTests
{
    [Fact]
    public void UntaggedAttachmentsMessage_includes_count()
    {
        var message = EmailMoveToProjectEligibilityRules.UntaggedAttachmentsMessage(3);

        Assert.Contains("3", message, StringComparison.Ordinal);
        Assert.Contains("לא מתויגות", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HasDuplicateFilingTargets_same_file_and_alt_is_duplicate()
    {
        Assert.True(EmailMoveToProjectEligibilityRules.HasDuplicateFilingTargets(
        [
            (10, 1),
            (10, 1),
        ]));
    }

    [Fact]
    public void HasDuplicateFilingTargets_same_file_both_default_alt_is_duplicate()
    {
        Assert.True(EmailMoveToProjectEligibilityRules.HasDuplicateFilingTargets(
        [
            (10, null),
            (10, 0),
        ]));
    }

    [Fact]
    public void HasDuplicateFilingTargets_different_alts_allowed()
    {
        Assert.False(EmailMoveToProjectEligibilityRules.HasDuplicateFilingTargets(
        [
            (10, 1),
            (10, 2),
        ]));
    }

    [Fact]
    public void HasDuplicateFilingTargets_different_files_allowed()
    {
        Assert.False(EmailMoveToProjectEligibilityRules.HasDuplicateFilingTargets(
        [
            (10, null),
            (11, null),
        ]));
    }
}

public sealed class SqlEmailMoveToProjectEligibilityServiceTests
{
    [Fact]
    public async Task EvaluateAsync_untagged_blocks_with_count_message()
    {
        var factory = await SeedAsync(
            new EmailInboxAttachment
            {
                Id = 1,
                MessageId = 50,
                AttachmentIndex = 0,
                SavedFileName = "a.pdf",
                ContentSha256 = new string('a', 64),
                AccItemId = "acc-a",
                ProjectFileId = null,
            },
            new EmailInboxAttachment
            {
                Id = 2,
                MessageId = 50,
                AttachmentIndex = 1,
                SavedFileName = "b.pdf",
                ContentSha256 = new string('b', 64),
                AccItemId = "acc-b",
                ProjectFileId = null,
            });

        var sut = new SqlEmailMoveToProjectEligibilityService(factory);
        var result = await sut.EvaluateAsync(
            new EmailMoveToProjectEligibilityQuery(50, 1042, AttachmentCount: 2, IsEmailFiledToProject: true));

        Assert.False(result.CanMove);
        Assert.Equal(EmailMoveToProjectEligibilityRules.UntaggedAttachmentsMessage(2), result.BlockReason);
    }

    [Fact]
    public async Task EvaluateAsync_duplicate_target_blocks()
    {
        var factory = await SeedAsync(
            new EmailInboxAttachment
            {
                Id = 1,
                MessageId = 50,
                AttachmentIndex = 0,
                SavedFileName = "a.pdf",
                ContentSha256 = new string('a', 64),
                AccItemId = "acc-a",
                ProjectFileId = 200,
                ProjectAlternativeId = 7,
            },
            new EmailInboxAttachment
            {
                Id = 2,
                MessageId = 50,
                AttachmentIndex = 1,
                SavedFileName = "b.pdf",
                ContentSha256 = new string('b', 64),
                AccItemId = "acc-b",
                ProjectFileId = 200,
                ProjectAlternativeId = 7,
            });

        var sut = new SqlEmailMoveToProjectEligibilityService(factory);
        var result = await sut.EvaluateAsync(
            new EmailMoveToProjectEligibilityQuery(50, 1042, AttachmentCount: 2, IsEmailFiledToProject: true));

        Assert.False(result.CanMove);
        Assert.Equal(EmailMoveToProjectEligibilityRules.DuplicateTargetMessage, result.BlockReason);
    }

    [Fact]
    public async Task EvaluateAsync_valid_tags_allows()
    {
        var factory = await SeedAsync(
            new EmailInboxAttachment
            {
                Id = 1,
                MessageId = 50,
                AttachmentIndex = 0,
                SavedFileName = "a.pdf",
                ContentSha256 = new string('a', 64),
                AccItemId = "acc-a",
                ProjectFileId = 200,
                ProjectAlternativeId = 7,
            },
            new EmailInboxAttachment
            {
                Id = 2,
                MessageId = 50,
                AttachmentIndex = 1,
                SavedFileName = "b.pdf",
                ContentSha256 = new string('b', 64),
                AccItemId = "acc-b",
                ProjectFileId = 201,
                ProjectAlternativeId = 7,
            });

        var sut = new SqlEmailMoveToProjectEligibilityService(factory);
        var result = await sut.EvaluateAsync(
            new EmailMoveToProjectEligibilityQuery(50, 1042, AttachmentCount: 2, IsEmailFiledToProject: true));

        Assert.True(result.CanMove);
        Assert.Null(result.BlockReason);
    }

    private static async Task<IDbContextFactory<SiNetSQLDbContext>> SeedAsync(
        params EmailInboxAttachment[] attachments)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.EmailInboxAttachments.AddRange(attachments);
            await seed.SaveChangesAsync();
        }

        return new StubDbContextFactory(options);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

public sealed class EmailMoveEligibilityGuardContractTests
{
    [Fact]
    public void Move_rechecks_eligibility_before_move()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");

        Assert.Contains("await RefreshMoveEligibilityAsync()", source, StringComparison.Ordinal);
        Assert.Contains("ActionBar.MoveBlockReason", source, StringComparison.Ordinal);
        Assert.Contains("RefreshMoveEligibilityThenActionBarAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_button_tooltip_binds_block_reason()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarViewModel.cs");
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarView.xaml");

        Assert.Contains("MoveButtonToolTip", vm, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding MoveButtonToolTip}\"", xaml, StringComparison.Ordinal);
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

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
