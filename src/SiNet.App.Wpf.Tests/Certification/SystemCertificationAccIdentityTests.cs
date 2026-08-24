using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationAccIdentityTests
{
    [Theory]
    [InlineData("62311c21-b79a-40e6-a992-18c6032314a0", "b.62311c21-b79a-40e6-a992-18c6032314a0")]
    [InlineData(" b.project-1 ", "b.project-1")]
    public void ProjectIdsMatch_treats_b_prefix_as_equivalent(string left, string right)
    {
        Assert.True(SystemCertificationAccIdentity.ProjectIdsMatch(left, right));
    }

    [Fact]
    public void ProjectIdsMatch_rejects_different_guids()
    {
        Assert.False(SystemCertificationAccIdentity.ProjectIdsMatch(
            "62311c21-b79a-40e6-a992-18c6032314a0",
            "b.40288036-a022-467b-8311-369d7a10fded"));
    }
}
