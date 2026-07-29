using SiNet.Application.Google;
using SiNet.Application.Runtime;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

/// <summary>
/// Guards the false-green fix: report generation fails on missing Shared Drive / root-folder write,
/// so the panel must not paint write-denied targets Idle (see <c>docs/SYSTEM_HEALTH.md</c> §2.4/§2.7).
/// </summary>
public sealed class DriveWriteStatusContributorTests
{
    [Fact]
    public void WhenFolderHasNoWriteAccessThenDescribeReportsDegradedNotIdle()
    {
        var (state, summary) = GoogleDriveFolderStatusContributorBase.Describe(
            new GoogleDriveFolderDiagnosticResult(
                GoogleDriveFolderStatus.NoWriteAccess,
                FolderName: "דוחות"));

        Assert.Equal(SubsystemRuntimeState.Degraded, state);
        Assert.Contains("אין הרשאת כתיבה", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenSharedDriveDeniesWriteThenMasterPlanRowIsDegradedWithExactUserMessage()
    {
        var options = new GmailOptions
        {
            ReportsSharedDriveId = "drive-abc",
            ReportsRootFolderId = "folder-xyz",
        };
        var diagnostics = new StubDiagnostics(
            sharedDrive: new GoogleDriveFolderDiagnosticResult(
                GoogleDriveFolderStatus.NoWriteAccess,
                FolderName: "SiNet Reports"),
            folder: new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.Ok));

        var contributor = new MasterPlanReportsDriveStatusContributor(options, diagnostics);
        var row = await contributor.ContributeAsync();

        Assert.Equal(MasterPlanReportsDriveStatusContributor.StatusKey, row.Key);
        Assert.Equal(SubsystemRuntimeState.Degraded, row.State);
        Assert.Equal("אין הרשאות כתיבה ל-Shared Drive", row.SummaryHe);
    }

    [Fact]
    public async Task WhenSharedDriveOkButRootFolderDeniesWriteThenMasterPlanRowIsDegraded()
    {
        var options = new GmailOptions
        {
            ReportsSharedDriveId = "drive-abc",
            ReportsRootFolderId = "folder-xyz",
        };
        var diagnostics = new StubDiagnostics(
            sharedDrive: new GoogleDriveFolderDiagnosticResult(
                GoogleDriveFolderStatus.Ok,
                FolderName: "SiNet Reports"),
            folder: new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.NoWriteAccess));

        var contributor = new MasterPlanReportsDriveStatusContributor(options, diagnostics);
        var row = await contributor.ContributeAsync();

        Assert.Equal(SubsystemRuntimeState.Degraded, row.State);
        Assert.Contains("תיקיית שורש הדוחות", row.SummaryHe, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenReportsNotConfiguredThenMasterPlanRowIsNotConfigured()
    {
        var contributor = new MasterPlanReportsDriveStatusContributor(
            new GmailOptions(),
            new StubDiagnostics(
                sharedDrive: new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.Ok),
                folder: new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.Ok)));

        var row = await contributor.ContributeAsync();

        Assert.Equal(SubsystemRuntimeState.NotConfigured, row.State);
    }

    private sealed class StubDiagnostics(
        GoogleDriveFolderDiagnosticResult sharedDrive,
        GoogleDriveFolderDiagnosticResult folder) : IGoogleDriveFolderDiagnostics
    {
        public Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(
            string? folderId,
            bool expectSpreadsheets,
            bool requireWriteAccess = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(folder);

        public Task<GoogleDriveFolderDiagnosticResult> DiagnoseSharedDriveWriteAsync(
            string? sharedDriveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(sharedDrive);
    }
}
