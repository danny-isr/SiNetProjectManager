using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Common;
using SiNet.Application.Inspection;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// LIVE INTEGRATION (product-path services), NOT LIVE UI.
/// Export on Report #9 via <see cref="IInspectionReportExportPort"/> (GoogleSheets port).
/// Proves artifact identity persistence. Does not mutate questionnaire notes —
/// no note restore in finally. Asserts SentAt / lock remain unchanged after export.
/// Share anyone-with-link is NOT invoked (safe boundary).
/// </summary>
[Collection(InspectionReport9LiveCollection.Name)]
public sealed class InspectionReport9LiveExportTests
{
    private const int ReportId = 9;

    [Fact]
    public async Task Export_creates_spreadsheet_without_locking_or_setting_SentAt()
    {
        var cs = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            return;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetLogging();
        services.AddSiNetSecrets();
        services.AddSiNetSql(cs!);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetInspectionSql();
        services.AddSiNetGoogle(static options =>
        {
            options.ApplicationName = "SiNet.InspectionExportLive";
            options.AllowInteractiveSignIn = false;
            options.TokenStorePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "google-token");
        });

        await using var sp = services.BuildServiceProvider();
        var workspace = sp.GetRequiredService<IInspectionWorkspace>();
        var google = sp.GetRequiredService<IConnectorAuthService>();
        _ = await google.TryRestoreSessionAsync().ConfigureAwait(true);

        var tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
        var numbered = tree.SelectMany(c => c.Sections).SelectMany(s => s.Notes)
            .Select(n => (n.Status, n.Text));
        Assert.True(
            InspectionQuestionnaireRules.CanExportNotes(numbered),
            "Report #9 must be exportable before live Export.");

        var detailBefore = await workspace.GetReportDetailAsync(ReportId).ConfigureAwait(true);
        Assert.NotNull(detailBefore);
        Assert.Null(detailBefore!.SentAt);
        Assert.False(detailBefore.IsLockedAfterSend);

        // No note mutation → no note restore. Export must leave SentAt / lock unchanged.
        var export = sp.GetRequiredService<IInspectionReportExportPort>();
        var result = await export.ExportAsync(ReportId).ConfigureAwait(true);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.SpreadsheetUrl));

        var detailAfter = await workspace.GetReportDetailAsync(ReportId).ConfigureAwait(true);
        Assert.NotNull(detailAfter);
        Assert.Null(detailAfter!.SentAt);
        Assert.False(detailAfter.IsLockedAfterSend);
        Assert.False(string.IsNullOrWhiteSpace(detailAfter.SentSpreadsheetUrl));

        // Safe boundary: do NOT call ShareAsync (anyone-with-link permission mutation).
        Assert.Contains("docs.google.com/spreadsheets", detailAfter.SentSpreadsheetUrl!, StringComparison.OrdinalIgnoreCase);
    }
}
