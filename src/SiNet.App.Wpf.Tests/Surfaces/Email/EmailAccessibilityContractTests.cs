using System.IO;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Tests.Boundary;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

/// <summary>
/// Source contracts for Email filter / ProjectSelector / list-row UIA identifiers.
/// </summary>
public sealed class EmailAccessibilityContractTests
{
    [Theory]
    [InlineData("Email.Filter.MailboxScope")]
    [InlineData("Email.Filter.Category")]
    [InlineData("Email.Filter.FreeText")]
    [InlineData("Email.Filter.Address")]
    [InlineData("Email.Filter.Subject")]
    [InlineData("Email.Filter.Label")]
    [InlineData("Email.Filter.ProjectLink")]
    [InlineData("Email.Filter.AttachmentsOnly")]
    [InlineData("Email.Action.Search")]
    [InlineData("Email.Action.ClearFilters")]
    [InlineData("Email.Action.Refresh")]
    public void EmailListFilterBar_declares_stable_AutomationId(string automationId)
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoPaths.RepoRoot,
            "src", "SiNet.App.Wpf", "Surfaces", "Email", "EmailListFilterBar.xaml"));
        Assert.Contains($"AutomationId=\"{automationId}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectSelector_Search_AutomationId_differs_from_Email_FreeText()
    {
        var projectSelector = File.ReadAllText(Path.Combine(
            RepoPaths.RepoRoot,
            "src", "SiNet.App.Wpf", "Shared", "Projects", "ProjectSelectorView.xaml"));
        var filterBar = File.ReadAllText(Path.Combine(
            RepoPaths.RepoRoot,
            "src", "SiNet.App.Wpf", "Surfaces", "Email", "EmailListFilterBar.xaml"));

        Assert.Contains("AutomationId=\"ProjectSelector.Search\"", projectSelector, StringComparison.Ordinal);
        Assert.Contains("AutomationId=\"Email.Filter.FreeText\"", filterBar, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationId=\"ProjectSelector.Search\"", filterBar, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailListView_binds_AutomationProperties_Name_to_AutomationName()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoPaths.RepoRoot,
            "src", "SiNet.App.Wpf", "Surfaces", "Email", "EmailListView.xaml"));
        Assert.Contains(
            "Property=\"AutomationProperties.Name\" Value=\"{Binding AutomationName}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmailListRow_AutomationName_uses_sender_and_subject_not_preview()
    {
        var row = new EmailListRow(
            "id-1",
            "alice@si-eng.co.il",
            "דוח ביקורת",
            "SECRET PREVIEW BODY SHOULD NOT APPEAR",
            DateTime.UtcNow,
            "Inbox",
            false,
            false,
            null,
            AttachmentCount: 0);

        Assert.Equal("alice@si-eng.co.il — דוח ביקורת", row.AutomationName);
        Assert.Equal(row.AutomationName, row.ToString());
        Assert.DoesNotContain("SECRET", row.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectSummaryDto_ToString_is_human_display()
    {
        var dto = new ProjectSummaryDto(
            ProjectId: 42,
            ProjectNumber: "1042",
            ProjectName: "פרויקט בדיקה",
            PlaceName: "SI",
            CompanyName: "שיא",
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true);

        Assert.Equal("1042 — פרויקט בדיקה", dto.ToString());
        Assert.DoesNotContain("ProjectSummaryDto", dto.ToString(), StringComparison.Ordinal);
    }
}
