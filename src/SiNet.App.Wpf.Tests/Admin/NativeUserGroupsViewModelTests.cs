using Moq;
using SiNet.App.Wpf.Admin.UserGroups;
using SiNet.App.Wpf.Admin.Users;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeUserGroupsViewModelTests
{
    [Fact]
    public async Task SelectedDefaultAssignee_change_persists_via_command_and_reloads_detail()
    {
        const int groupId = 10;
        var memberA = new UserGroupMemberDto(1, "Alice", true);
        var memberB = new UserGroupMemberDto(2, "Bob", true);

        var setCalls = new List<int?>();
        var detailCalls = 0;

        var query = new Mock<IUserGroupQueryService>();
        query.Setup(q => q.GetActiveGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new UserGroupSummaryDto(groupId, "ENG", "Engineers", null, null, null, 2),
            ]);
        query.Setup(q => q.GetGroupDetailAsync(groupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                detailCalls++;
                var defaultId = setCalls.Count > 0 ? setCalls[^1] : null;
                return new UserGroupDetailDto(
                    groupId,
                    "ENG",
                    "Engineers",
                    null,
                    defaultId,
                    [memberA, memberB],
                    []);
            });

        var command = new Mock<IUserGroupCommandService>();
        command.Setup(c => c.SetDefaultAssigneeAsync(groupId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int?, CancellationToken>((_, userId, _) => setCalls.Add(userId))
            .Returns(Task.CompletedTask);

        var lookup = new Mock<IUserLookupService>();
        lookup.Setup(l => l.GetActiveUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new UserGroupsViewModel(query.Object, command.Object, lookup.Object);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Members.Count);

        vm.SelectedDefaultAssignee = memberB;
        await WaitUntilAsync(() => setCalls.Count >= 1 && !vm.IsBusy, TimeSpan.FromSeconds(3));

        Assert.Contains(2, setCalls);
        Assert.True(detailCalls >= 2);
        Assert.Equal(2, vm.SelectedDefaultAssignee?.UserId);
        Assert.Contains("ברירת מחדל עודכנה", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsersChanged_refreshes_AvailableUsers_for_selected_group()
    {
        const int groupId = 10;
        var memberA = new UserGroupMemberDto(1, "Alice", true);
        var lookupCalls = 0;

        var query = new Mock<IUserGroupQueryService>();
        query.Setup(q => q.GetActiveGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new UserGroupSummaryDto(groupId, "ENG", "Engineers", null, null, null, 1),
            ]);
        query.Setup(q => q.GetGroupDetailAsync(groupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserGroupDetailDto(
                groupId,
                "ENG",
                "Engineers",
                null,
                null,
                [memberA],
                []));

        var command = new Mock<IUserGroupCommandService>();
        var lookup = new Mock<IUserLookupService>();
        lookup.Setup(l => l.GetActiveUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lookupCalls++;
                if (lookupCalls == 1)
                {
                    return [new UserLookupDto(1, "Alice", true)];
                }

                return
                [
                    new UserLookupDto(1, "Alice", true),
                    new UserLookupDto(3, "Carol", true),
                ];
            });

        var notifier = new UserAdminChangesNotifier();
        var vm = new UserGroupsViewModel(query.Object, command.Object, lookup.Object, notifier);
        await vm.LoadAsync();
        Assert.Empty(vm.AvailableUsers);

        notifier.NotifyUsersChanged();
        await WaitUntilAsync(() => vm.AvailableUsers.Any(u => u.UserId == 3), TimeSpan.FromSeconds(3));

        Assert.Contains(vm.AvailableUsers, u => u.UserId == 3 && u.DisplayName == "Carol");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for condition.");
    }
}
