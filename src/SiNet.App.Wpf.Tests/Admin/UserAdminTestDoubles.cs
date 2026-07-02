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
}
