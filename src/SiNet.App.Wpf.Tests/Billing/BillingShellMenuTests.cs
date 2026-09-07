using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Tests.Shell;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingShellMenuTests
{
    [Fact]
    public void Authorized_user_gets_finance_group_with_billing_center()
    {
        var top = BuildMenu(allowBilling: true);
        var finance = Assert.Single(top, g => g.Title == "כספים");
        Assert.Contains(finance.Children, i => i.Title == "מרכז חיובים" && i.IsAvailable);
        Assert.Equal(
            "ריכוז פרויקטים לבדיקה לחשבון, חשבונות ונתוני גבייה",
            finance.Children.Single(i => i.Title == "מרכז חיובים").Description);
    }

    [Fact]
    public void Unauthorized_user_does_not_get_finance_group()
    {
        var top = BuildMenu(allowBilling: false);
        Assert.DoesNotContain(top, g => g.Title == "כספים");
        Assert.DoesNotContain(NewShellMenuReflection.Flatten(top), i => i.Title == "מרכז חיובים");
    }

    [Fact]
    public void Billing_center_is_not_under_reports()
    {
        var top = BuildMenu(allowBilling: true);
        var reports = Assert.Single(top, g => g.Title == "דוחות");
        Assert.DoesNotContain(reports.Children, i => i.Title == "מרכז חיובים");
        Assert.DoesNotContain(reports.Children, i => i.Title.Contains("חיוב", StringComparison.Ordinal));
        Assert.Contains(top, g => g.Title == "כספים");
    }

    [Fact]
    public void NewShellFactory_opens_billing_window_via_feature_code()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("AppFeatureCodes.ShellOpenBillingCenter", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeBillingDashboard", source, StringComparison.Ordinal);
        Assert.Contains("BillingDashboardWindow", source, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<BillingDashboardWindow>()", source, StringComparison.Ordinal);
        Assert.Contains("כספים", source, StringComparison.Ordinal);
        Assert.Contains("מרכז חיובים", source, StringComparison.Ordinal);
        var nativeStart = source.IndexOf("private void OpenNativeBillingDashboard", StringComparison.Ordinal);
        Assert.True(nativeStart >= 0);
        var debugAfterNative = source.IndexOf("#if DEBUG", nativeStart, StringComparison.Ordinal);
        var personal = source.IndexOf("private void OpenNativePersonalSettings", nativeStart, StringComparison.Ordinal);
        var nativeEnd = debugAfterNative >= 0 && debugAfterNative < personal ? debugAfterNative : personal;
        Assert.True(nativeEnd > nativeStart);
        var nativeMethod = source[nativeStart..nativeEnd];
        Assert.Contains("GetRequiredService<BillingDashboardWindow>()", nativeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("new BillingDashboardWindow", nativeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("BillingDashboardHealthyVisualFixture", nativeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportsManagement", source.Substring(source.IndexOf("כספים", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void B4_does_not_add_b5_persistence_or_actions()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardViewModel.cs");
        var factory = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.DoesNotContain("PrepareBill", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("NotNow", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("IBillingDecision", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewAgainDate", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareBill", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("BillingHold", factory, StringComparison.Ordinal);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenu(bool allowBilling)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(allowBilling));
        services.AddSingleton<ICurrentUserContext>(new StubUserContext(1));
        var sp = services.BuildServiceProvider();
        return NewShellMenuReflection.Build(new NewShellFactory(sp));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubAuthorization(bool allowBilling) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(allowBilling && featureCode == AppFeatureCodes.ShellOpenBillingCenter);
    }

    private sealed class StubUserContext(int? userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }
}
