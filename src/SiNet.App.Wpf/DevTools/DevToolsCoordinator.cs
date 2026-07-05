using System.Text;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Theme;
using SiNet.Application.DevTools;

namespace SiNet.App.Wpf.DevTools;

/// <summary>
/// Coordinates New System dev reset/seed operations from the shell (Application ports only).
/// </summary>
public sealed class DevToolsCoordinator(IServiceProvider services)
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public async Task RunResetWithDialogAsync(Window? owner)
    {
        var reset = _services.GetService<IDevDataResetService>();
        if (reset is null)
        {
            ShowError(owner, "IDevDataResetService לא רשום.");
            return;
        }

        if (!reset.IsCurrentUserAllowed())
        {
            ShowError(owner, $"איפוס נתונים מוגבל. המשתמש '{reset.CurrentWindowsUser}' אינו ברשימת המורשים.");
            return;
        }

        ThemeResourceLoader.EnsureApplicationResourcesMerged();

        var dbName = await reset.PeekDatabaseNameAsync().ConfigureAwait(true) ?? "(unknown)";
        var dialog = new ResetOptionsDialog(dbName, reset.CurrentWindowsUser)
        {
            Owner = owner,
        };

        if (dialog.ShowDialog() != true || !dialog.UserApproved)
            return;

        try
        {
            var options = new DevDataResetOptions
            {
                PreserveSystemSettings = !dialog.WipeSystemSettings,
                ResetUserSettings = dialog.ResetUserSettings,
                IncludeTaskSeed = true,
                IncludeMappingsSeed = true,
                IncludeWorkflowSeed = true,
                IncludeDemoTasks = dialog.IncludeDemoTasks,
            };

            var report = await reset.ResetAsync(options).ConfigureAwait(true);
            MessageBox.Show(
                owner,
                FormatResetReport(report),
                "איפוס נתונים — סיכום",
                MessageBoxButton.OK,
                report.FailedTableCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowError(owner, ex.Message);
        }
    }

    public async Task RunCoreSeedAsync(Window? owner)
    {
        var seed = _services.GetRequiredService<IStaticSeedService>();
        try
        {
            var result = await seed.SeedAllCoreAsync().ConfigureAwait(true);
            MessageBox.Show(owner, result.Summary, "Seed בסיסי", MessageBoxButton.OK,
                result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowError(owner, ex.Message);
        }
    }

    public async Task RunDemoTaskSeedAsync(Window? owner)
    {
        var seed = _services.GetRequiredService<IStaticSeedService>();
        try
        {
            var result = await seed.SeedDemoTasksAsync().ConfigureAwait(true);
            if (!result.Succeeded)
            {
                var message = result.Errors.Count > 0
                    ? string.Join(Environment.NewLine, result.Errors.Prepend(result.Summary))
                    : result.Summary;
                ShowError(owner, message);
                return;
            }

            MessageBox.Show(owner, result.Summary, "משימות דמו", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (DbUpdateException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ShowError(owner,
                "טעינת משימות דמו נכשלה. ייתכן שקיימת כפילות לפי IX_ProjectAssignment_UniqueOpenTask.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ShowError(owner, ex.Message);
        }
    }

    internal static string FormatResetReport(DevDataResetResult report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DB: {report.DatabaseName}");
        sb.AppendLine($"משך: {report.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"שורות שנמחקו: {report.TotalRowsDeleted}");
        sb.AppendLine($"טבלאות שנכשלו: {report.FailedTableCount}");
        sb.AppendLine($"SystemSettings נשמר: {report.SystemSettingsPreserved}");
        sb.AppendLine($"UserSettings נשמר: {report.UserSettingsPreserved}");
        sb.AppendLine($"Seed: {report.SeedApplied}  Mappings: {report.MappingsApplied}  Workflow: {report.WorkflowSeedApplied}  Demo: {report.DemoTasksSeedApplied}");
        if (report.Errors.Count > 0)
            sb.AppendLine(string.Join(Environment.NewLine, report.Errors));
        return sb.ToString();
    }

    private static void ShowError(Window? owner, string message) =>
        MessageBox.Show(owner, message, "כלי פיתוח", MessageBoxButton.OK, MessageBoxImage.Error);
}
