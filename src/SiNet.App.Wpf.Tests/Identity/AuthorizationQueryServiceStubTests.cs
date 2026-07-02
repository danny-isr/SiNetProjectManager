using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class AuthorizationQueryServiceStubTests
{
    private sealed class StubAuthorizationQueryService(AppRole? role) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(
            AppRole requiredRole,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(role is AppRole current
                && AppFeatureAuthorization.SatisfiesRole(current, requiredRole));
        }

        public Task<bool> CanCurrentUserAccessFeatureAsync(
            string featureCode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (role is not AppRole current)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(AppFeatureAuthorization.CanAccessFeature(current, featureCode));
        }
    }

    [Fact]
    public async Task No_authenticated_user_denies_role_and_feature()
    {
        var sut = new StubAuthorizationQueryService(null);

        Assert.False(await sut.IsCurrentUserInRoleAsync(AppRole.Employee));
        Assert.False(await sut.CanCurrentUserAccessFeatureAsync(AppFeatureCodes.ShellOpenEmailSurface));
    }

    [Fact]
    public async Task Unknown_feature_code_throws_from_service_path()
    {
        var sut = new StubAuthorizationQueryService(AppRole.Administrator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CanCurrentUserAccessFeatureAsync("Unknown.Feature.Code"));
    }
}
