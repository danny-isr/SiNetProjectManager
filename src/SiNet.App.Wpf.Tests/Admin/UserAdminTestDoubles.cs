using Moq;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Tests.Admin;

internal static class UserAdminTestDoubles
{
    internal static IMasterPlanEmployeeLookupService EmptyMasterPlanLookup()
    {
        var lookup = new Mock<IMasterPlanEmployeeLookupService>();
        lookup.Setup(s => s.GetEmployeesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MasterPlanEmployeeDto(null, "-- ללא קישור --")]);
        return lookup.Object;
    }

    internal static IDirectoryUserLookupService EmptyDirectoryLookup()
    {
        var lookup = new Mock<IDirectoryUserLookupService>();
        lookup.SetupGet(s => s.IsConfigured).Returns(false);
        lookup.Setup(s => s.SearchUsersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DirectoryUserDto>());
        return lookup.Object;
    }

    internal static IDirectoryUserLookupService DirectoryLookupWith(params DirectoryUserDto[] users)
    {
        var lookup = new Mock<IDirectoryUserLookupService>();
        lookup.SetupGet(s => s.IsConfigured).Returns(true);
        lookup.Setup(s => s.SearchUsersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) =>
                users.Where(u =>
                        u.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || u.LoginName.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || (u.Email?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList());
        return lookup.Object;
    }
}
