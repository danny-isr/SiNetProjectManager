using System.IO;
using SiNet.Application.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailSuggestedActionsBuilderTests
{
    [Fact]
    public void Unassigned_context_returns_legacy_inbox_actions()
    {
        var context = new EmailWorkflowContextDto(
            HasContext: true,
            ProjectDisplay: "לא משויך לפרויקט",
            WorkflowFamilyDisplay: null,
            ConfidenceDisplay: null,
            ActiveWorkflowCount: 0,
            AttachmentCount: 2,
            IsAssociatedToProject: false);

        var actions = EmailSuggestedActionsBuilder.Build(context);

        Assert.Contains(actions, a => a.ActionCode == EmailSuggestedActionCodes.AssociateToExistingProject);
        Assert.Contains(actions, a => a.ActionCode == EmailSuggestedActionCodes.CreatePriceQuote);
        Assert.Contains(actions, a => a.ActionCode == EmailSuggestedActionCodes.CreateNewReview);
        Assert.Contains(actions, a => a.ActionCode == EmailSuggestedActionCodes.RequestAuthorityInvitation);
        Assert.Contains(actions, a => a.ActionCode == EmailSuggestedActionCodes.CollectMaterial);
        Assert.Contains(actions, a => a.ActionCode == EmailSuggestedActionCodes.FileOnly);
        Assert.DoesNotContain(actions, a => a.ActionCode == Application.Actions.ProcessActionCodes.SetProjectStatus);
    }

    [Fact]
    public void Associated_context_returns_foundation_process_actions()
    {
        var context = new EmailWorkflowContextDto(
            HasContext: true,
            ProjectDisplay: "1042 — North",
            WorkflowFamilyDisplay: null,
            ConfidenceDisplay: "בינונית",
            ActiveWorkflowCount: 1,
            AttachmentCount: 1,
            IsAssociatedToProject: true);

        var actions = EmailSuggestedActionsBuilder.Build(context);

        Assert.Contains(actions, a => a.ActionCode == Application.Actions.ProcessActionCodes.SendNotification);
        Assert.Contains(actions, a => a.ActionCode == Application.Actions.ProcessActionCodes.RecordTaskResult);
        Assert.Contains(actions, a => a.ActionCode == Application.Actions.ProcessActionCodes.SetProjectStatus);
        Assert.DoesNotContain(actions, a => a.ActionCode == EmailSuggestedActionCodes.CreatePriceQuote);
    }
}

public sealed class EmailDetailSelectionRefreshContractTests
{
    [Fact]
    public void ApplySelection_tracks_loaded_body_message_id()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");

        Assert.Contains("_loadedBodyMessageId", source, StringComparison.Ordinal);
        Assert.Contains("IsCurrentSelection", source, StringComparison.Ordinal);
        Assert.Contains("RefreshInboxAttachmentsAsync(loadVersion, row.Id)", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_loadedBodyMessageId, messageId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewer_requires_html_before_rich_renderer()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailViewerPaneViewModel.cs");

        Assert.Contains("string.IsNullOrWhiteSpace(_htmlBody)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Action_bar_splits_unassigned_and_assigned_layout()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarViewModel.cs");
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarView.xaml");
        Assert.Contains("ShowUnassignedLayout", vm, StringComparison.Ordinal);
        Assert.Contains("ShowAssignedLayout", vm, StringComparison.Ordinal);
        Assert.Contains("ShowUnassignedLayout", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowAssignedLayout", xaml, StringComparison.Ordinal);
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
