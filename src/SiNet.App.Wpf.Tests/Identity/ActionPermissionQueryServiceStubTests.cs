using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

/// <summary>
/// Contract tests for <see cref="IActionPermissionQueryService"/> using an in-memory stub.
/// Full legacy semantics are covered in <c>LegacyActionPermissionQueryServiceTests</c> (SiNetSQL.Tests).
/// </summary>
public sealed class ActionPermissionQueryServiceStubTests
{
    private sealed class StubCurrentUserContext(int? userId) : ICurrentUserContext
    {
        public int? UserId => userId;
    }

    private sealed class StubActionPermissionQueryService(
        ICurrentUserContext currentUser,
        Func<string, int, bool>? canUserExecute = null,
        Func<string, IReadOnlyList<UserRefDto>>? getAuthorized = null)
        : IActionPermissionQueryService
    {
        public Task<bool> CanUserExecuteActionAsync(
            string actionCode,
            int userId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(canUserExecute?.Invoke(actionCode, userId) ?? false);
        }

        public Task<bool> CanCurrentUserExecuteActionAsync(
            string actionCode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentUser.UserId is not int userId)
            {
                return Task.FromResult(false);
            }

            return CanUserExecuteActionAsync(actionCode, userId, cancellationToken);
        }

        public Task<IReadOnlyList<UserRefDto>> GetAuthorizedUsersForActionAsync(
            string actionCode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(getAuthorized?.Invoke(actionCode) ?? Array.Empty<UserRefDto>());
        }
    }

    [Fact]
    public async Task CanCurrentUserExecuteActionAsync_returns_false_when_UserId_is_null()
    {
        var sut = new StubActionPermissionQueryService(
            new StubCurrentUserContext(null),
            canUserExecute: (_, _) => true);

        var result = await sut.CanCurrentUserExecuteActionAsync(ActionPermissionCodes.NewProjectDialog);

        Assert.False(result);
    }

    [Fact]
    public async Task CanCurrentUserExecuteActionAsync_delegates_to_user_check_when_UserId_present()
    {
        const int userId = 42;
        var sut = new StubActionPermissionQueryService(
            new StubCurrentUserContext(userId),
            canUserExecute: (code, id) =>
                code == ActionPermissionCodes.ProjectPicker && id == userId);

        Assert.True(await sut.CanCurrentUserExecuteActionAsync(ActionPermissionCodes.ProjectPicker));
        Assert.False(await sut.CanCurrentUserExecuteActionAsync(ActionPermissionCodes.NewProjectDialog));
    }

    [Fact]
    public void ActionPermissionCodes_lists_all_legacy_ActionFollowUp_names()
    {
        Assert.True(ActionPermissionCodes.IsKnownActionCode(ActionPermissionCodes.NewProjectDialog));
        Assert.True(ActionPermissionCodes.IsKnownActionCode(ActionPermissionCodes.WorkflowAdvanceDialog));
        Assert.False(ActionPermissionCodes.IsKnownActionCode("NotARealAction"));
        Assert.False(ActionPermissionCodes.IsKnownActionCode(""));
    }
}
