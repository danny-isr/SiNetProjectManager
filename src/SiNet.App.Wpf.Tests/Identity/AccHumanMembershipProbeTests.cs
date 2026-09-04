using SiNet.Application.Identity;
using SiNet.Infrastructure.AccBootstrap;
using SiNetSQL.Services.AccBootstrap;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class AccHumanMembershipProbeTests
{
    [Fact]
    public async Task When_list_members_throws_then_probe_unavailable_not_absent()
    {
        var probe = new AccHumanMembershipProbe(new ThrowingListProvisioning());

        var result = await probe.ProbeAsync(
            "acc-project-1",
            "shirly@si-eng.co.il",
            allowReconcile: true);

        Assert.NotNull(result);
        Assert.False(result!.ProbeSucceeded);
        Assert.False(result.IsMember);
        Assert.Null(result.MatchedMemberEmail);
        Assert.Contains("list failed", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_reconcile_throws_after_empty_list_then_probe_unavailable()
    {
        var probe = new AccHumanMembershipProbe(new EmptyThenThrowReconcileProvisioning());

        var result = await probe.ProbeAsync(
            "acc-project-1",
            "shirly@si-eng.co.il",
            allowReconcile: true);

        Assert.NotNull(result);
        Assert.False(result!.ProbeSucceeded);
        Assert.False(result.IsMember);
        Assert.True(result.ReconcileAttempted);
        Assert.Contains("reconcile failed", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    private abstract class StubProvisioning : IAccProjectProvisioningService
    {
        public virtual Task<ProjectAccTargets> EnsureProjectMappingAsync(int projectId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public virtual Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public virtual Task<bool> EnsureCustomAttributeDefinitionsAsync(
            string accProjectId, string accFolderId, int? siProjectId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public virtual Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);

        public virtual Task<string> ProbeFolderPermissionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);

        public virtual Task<string> ProbeFolderPermissionsFromTemplateAsync(
            string templateName, CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);

        public virtual Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(string Id, string Name)>>([]);

        public abstract Task<IReadOnlyList<AccProjectMemberInfo>> ListProjectMembersAsync(
            string accProjectId, CancellationToken cancellationToken);
    }

    private sealed class ThrowingListProvisioning : StubProvisioning
    {
        public override Task<IReadOnlyList<AccProjectMemberInfo>> ListProjectMembersAsync(
            string accProjectId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("list failed: HTTP 403 Forbidden");
    }

    private sealed class EmptyThenThrowReconcileProvisioning : StubProvisioning
    {
        public override Task<IReadOnlyList<AccProjectMemberInfo>> ListProjectMembersAsync(
            string accProjectId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AccProjectMemberInfo>>([]);

        public override Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("reconcile failed: HTTP 403 Forbidden");
    }
}
