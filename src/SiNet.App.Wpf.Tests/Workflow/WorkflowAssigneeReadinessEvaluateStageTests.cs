using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class WorkflowAssigneeReadinessEvaluateStageTests
{
    [Fact]
    public void EvaluateStage_missing_assigned_group_returns_MissingAssignedGroup()
    {
        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = null,
            IsFinal = false,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage("PRP", stage, new Dictionary<int, UserGroup>());

        Assert.NotNull(issue);
        Assert.Equal(WorkflowAssigneeIssueKind.MissingAssignedGroup, issue!.IssueKind);
        Assert.Null(issue.GroupCode);
    }

    [Fact]
    public void EvaluateStage_group_missing_returns_GroupMissing()
    {
        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = 99,
            IsFinal = false,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage("PRP", stage, new Dictionary<int, UserGroup>());

        Assert.NotNull(issue);
        Assert.Equal(WorkflowAssigneeIssueKind.GroupMissing, issue!.IssueKind);
    }

    [Fact]
    public void EvaluateStage_zero_active_members_returns_NoActiveMembers()
    {
        var group = new UserGroup
        {
            Id = 1,
            Code = "Planners",
            Name = "מתכננים",
            Memberships =
            [
                new UserGroupMembership { SiuserId = 10, Siuser = new Siuser { Id = 10, Name = "Inactive", IsActive = false } },
            ],
        };
        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = 1,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage(
            "PRP",
            stage,
            new Dictionary<int, UserGroup> { [1] = group });

        Assert.NotNull(issue);
        Assert.Equal(WorkflowAssigneeIssueKind.NoActiveMembers, issue!.IssueKind);
        Assert.Equal("Planners", issue.GroupCode);
    }

    [Fact]
    public void EvaluateStage_multiple_members_without_default_returns_MultipleMembersWithoutDefault()
    {
        var group = CreateGroupWithMembers(defaultAssigneeId: null, activeCount: 2);

        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = group.Id,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage(
            "PRP",
            stage,
            new Dictionary<int, UserGroup> { [group.Id] = group });

        Assert.NotNull(issue);
        Assert.Equal(WorkflowAssigneeIssueKind.MultipleMembersWithoutDefault, issue!.IssueKind);
    }

    [Fact]
    public void EvaluateStage_multiple_members_with_invalid_default_returns_MultipleMembersWithoutDefault()
    {
        var group = CreateGroupWithMembers(defaultAssigneeId: 999, activeCount: 2);

        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = group.Id,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage(
            "PRP",
            stage,
            new Dictionary<int, UserGroup> { [group.Id] = group });

        Assert.NotNull(issue);
        Assert.Equal(WorkflowAssigneeIssueKind.MultipleMembersWithoutDefault, issue!.IssueKind);
    }

    [Fact]
    public void EvaluateStage_single_active_member_is_ok()
    {
        var group = CreateGroupWithMembers(defaultAssigneeId: null, activeCount: 1);
        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = group.Id,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage(
            "PRP",
            stage,
            new Dictionary<int, UserGroup> { [group.Id] = group });

        Assert.Null(issue);
    }

    [Fact]
    public void EvaluateStage_multiple_members_with_valid_default_is_ok()
    {
        var group = CreateGroupWithMembers(defaultAssigneeId: 1, activeCount: 2);
        var stage = new WorkflowStageDefinition
        {
            Code = "Calculation",
            Name = "אומדן",
            AssignedGroupId = group.Id,
        };

        var issue = SqlWorkflowAssigneeReadinessQueryService.EvaluateStage(
            "PRP",
            stage,
            new Dictionary<int, UserGroup> { [group.Id] = group });

        Assert.Null(issue);
    }

    private static UserGroup CreateGroupWithMembers(int? defaultAssigneeId, int activeCount)
    {
        var memberships = new List<UserGroupMembership>();
        for (var i = 1; i <= activeCount; i++)
        {
            memberships.Add(new UserGroupMembership
            {
                SiuserId = i,
                Siuser = new Siuser { Id = i, Name = $"User {i}", IsActive = true },
            });
        }

        return new UserGroup
        {
            Id = 7,
            Code = "Planners",
            Name = "מתכננים",
            DefaultAssigneeId = defaultAssigneeId,
            Memberships = memberships,
        };
    }
}
