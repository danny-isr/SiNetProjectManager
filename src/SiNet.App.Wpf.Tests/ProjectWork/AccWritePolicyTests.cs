using SiNet.Application.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class AccWritePolicyTests
{
    [Fact]
    public void Closed_policy_blocks_and_reports_operation()
    {
        var policy = new StaticAccWritePolicy(isWriteEnabled: false);
        Assert.False(policy.IsWriteEnabled);

        var ex = Assert.Throws<AccWriteGatedException>(() => policy.EnsureWriteAllowed("acc-upload"));
        Assert.Equal("acc-upload", ex.Operation);
    }

    [Fact]
    public void Open_policy_allows_writes()
    {
        var policy = new StaticAccWritePolicy(isWriteEnabled: true);
        Assert.True(policy.IsWriteEnabled);
        policy.EnsureWriteAllowed("acc-upload"); // must not throw
    }
}
