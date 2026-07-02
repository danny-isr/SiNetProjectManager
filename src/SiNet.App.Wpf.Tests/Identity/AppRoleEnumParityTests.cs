using SiNet.Application.Identity;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

/// <summary>
/// Guards numeric/name parity between Application identity enums and legacy DB enums.
/// Adapters cast via <c>(int)</c>; drift here breaks authorization silently.
/// </summary>
public sealed class AppRoleEnumParityTests
{
    [Fact]
    public void AppRole_values_match_AppUserRole()
    {
        Assert.Equal(Enum.GetNames<AppRole>().Length, Enum.GetNames<AppUserRole>().Length);

        foreach (AppRole appRole in Enum.GetValues<AppRole>())
        {
            var legacyName = Enum.GetName(typeof(AppUserRole), (AppUserRole)(int)appRole);
            Assert.Equal(Enum.GetName(appRole), legacyName);
            Assert.Equal((int)appRole, (int)(AppUserRole)(int)appRole);
        }
    }

    [Fact]
    public void AppAccUserType_values_match_AccUserType()
    {
        Assert.Equal(Enum.GetNames<AppAccUserType>().Length, Enum.GetNames<AccUserType>().Length);

        foreach (AppAccUserType appType in Enum.GetValues<AppAccUserType>())
        {
            var legacyName = Enum.GetName(typeof(AccUserType), (AccUserType)(int)appType);
            Assert.Equal(Enum.GetName(appType), legacyName);
            Assert.Equal((int)appType, (int)(AccUserType)(int)appType);
        }
    }

    [Theory]
    [InlineData(AppRole.Unauthorized, AppUserRole.Unauthorized)]
    [InlineData(AppRole.Employee, AppUserRole.Employee)]
    [InlineData(AppRole.Management, AppUserRole.Management)]
    [InlineData(AppRole.Administrator, AppUserRole.Administrator)]
    public void AppRole_casts_to_AppUserRole(AppRole appRole, AppUserRole expectedLegacy)
    {
        Assert.Equal(expectedLegacy, (AppUserRole)(int)appRole);
        Assert.Equal(appRole, (AppRole)(int)expectedLegacy);
    }
}
