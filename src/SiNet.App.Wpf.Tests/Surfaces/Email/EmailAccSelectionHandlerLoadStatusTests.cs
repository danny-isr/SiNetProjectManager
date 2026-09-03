using System.Net.Http;
using Moq;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailAccSelectionHandlerLoadStatusTests
{
    [Fact]
    public async Task WhenStatusSyncSucceedsThenLoadingClearedAndDisplayApplied()
    {
        var status = new EmailAccInboxStatus(
            "msg@test.com",
            1,
            EmailAccProcessingStatus.UploadedToAcc,
            null,
            "קיים ב-ACC",
            null,
            1,
            1,
            0,
            []);

        var statusService = new Mock<IEmailAccStatusService>();
        statusService
            .Setup(s => s.SyncStatusWithRecoveryAsync(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        EmailListRow? patched = null;
        var handler = new EmailAccSelectionHandler(statusService.Object, uploadCoordinator: null, patch => patched = patch);
        var row = SampleRow();

        var (result, returned) = await handler.LoadStatusAsync(row);

        Assert.Same(status, returned);
        Assert.False(result.IsAccStatusLoading);
        Assert.Equal("קיים ב-ACC", result.AccStatusDisplay);
        Assert.NotNull(patched);
        Assert.False(patched!.IsAccStatusLoading);
    }

    [Fact]
    public async Task WhenStatusServiceThrowsHttpThenOperatorSeesUnavailableAndLoadingCleared()
    {
        var statusService = new Mock<IEmailAccStatusService>();
        statusService
            .Setup(s => s.SyncStatusWithRecoveryAsync(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No connection could be made"));

        EmailListRow? patched = null;
        string? statusMessage = null;
        var handler = new EmailAccSelectionHandler(statusService.Object, uploadCoordinator: null, patch => patched = patch);
        handler.StatusMessageChanged += msg => statusMessage = msg;

        var (result, returned) = await handler.LoadStatusAsync(SampleRow());

        Assert.Null(returned);
        Assert.False(result.IsAccStatusLoading);
        Assert.Equal(EmailAccSelectionHandler.AccUnavailableOperatorMessage, result.AccStatusDisplay);
        Assert.Equal(EmailAccSelectionHandler.AccUnavailableOperatorMessage, statusMessage);
        Assert.False(patched!.IsAccStatusLoading);
        Assert.DoesNotContain("No connection", result.AccStatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenStatusSyncTimesOutThenOperatorSeesUnavailableAndLoadingCleared()
    {
        var statusService = new Mock<IEmailAccStatusService>();
        statusService
            .Setup(s => s.SyncStatusWithRecoveryAsync(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        EmailListRow? patched = null;
        var handler = new EmailAccSelectionHandler(statusService.Object, uploadCoordinator: null, patch => patched = patch);

        var (result, returned) = await handler.LoadStatusAsync(SampleRow(), CancellationToken.None);

        Assert.Null(returned);
        Assert.False(result.IsAccStatusLoading);
        Assert.Equal(EmailAccSelectionHandler.AccUnavailableOperatorMessage, result.AccStatusDisplay);
        Assert.False(patched!.IsAccStatusLoading);
    }

    [Fact]
    public async Task WhenSelectionCancelledThenLoadingClearedAndExceptionRethrown()
    {
        using var cts = new CancellationTokenSource();
        var statusService = new Mock<IEmailAccStatusService>();
        statusService
            .Setup(s => s.SyncStatusWithRecoveryAsync(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string? _, string _, string? _, CancellationToken ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return null;
            });

        EmailListRow? patched = null;
        var handler = new EmailAccSelectionHandler(statusService.Object, uploadCoordinator: null, patch => patched = patch);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.LoadStatusAsync(SampleRow(), cts.Token));

        Assert.NotNull(patched);
        Assert.False(patched!.IsAccStatusLoading);
    }

    [Fact]
    public void IsAccServiceUnavailable_distinguishesTimeoutFromSelectionCancel()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.True(EmailAccSelectionHandler.IsAccServiceUnavailable(
            new OperationCanceledException(),
            CancellationToken.None));
        Assert.False(EmailAccSelectionHandler.IsAccServiceUnavailable(
            new OperationCanceledException(),
            cts.Token));
        Assert.True(EmailAccSelectionHandler.IsAccServiceUnavailable(
            new HttpRequestException("down"),
            CancellationToken.None));
    }

    private static EmailListRow SampleRow() =>
        new(
            "gmail-1",
            "sender@test.com",
            "Subject",
            "Preview",
            DateTime.UtcNow,
            "Inbox",
            false,
            false,
            null,
            AttachmentCount: 0,
            InternetMessageId: "<msg@test.com>");
}
