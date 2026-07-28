using Moq;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

/// <summary>
/// Offline ACC status / upload-disabled reasons for the Email workbench.
/// See <c>docs/TEST_STRATEGY.md</c> L3.
/// </summary>
public sealed class EmailAccSelectionHandlerStatusTests
{
    [Fact]
    public void WhenBackendMissingThenDescribeUploadDisabledReasonReportsUnavailable()
    {
        var handler = new EmailAccSelectionHandler(
            statusService: null,
            uploadCoordinator: null);

        var reason = handler.DescribeUploadDisabledReason(RowWithAttachments(), isConnected: true);

        Assert.Equal("העלאה ל-ACC אינה זמינה.", reason);
        Assert.False(handler.CanUpload(RowWithAttachments(), isConnected: true));
    }

    [Fact]
    public void WhenUploadedToAccThenDescribeUploadDisabledReasonReportsAlreadyUploaded()
    {
        var handler = CreateHandlerWithCoordinator();
        var row = RowWithAttachments() with
        {
            AccProcessingStatus = EmailAccProcessingStatus.UploadedToAcc,
        };

        Assert.Equal("כבר הועלה ל-ACC.", handler.DescribeUploadDisabledReason(row, isConnected: true));
        Assert.False(handler.CanUpload(row, isConnected: true));
    }

    [Fact]
    public void WhenMovedToProjectThenDescribeUploadDisabledReasonReportsAlreadyUploaded()
    {
        var handler = CreateHandlerWithCoordinator();
        var row = RowWithAttachments() with
        {
            AccProcessingStatus = EmailAccProcessingStatus.MovedToProject,
        };

        Assert.Equal("כבר הועלה ל-ACC.", handler.DescribeUploadDisabledReason(row, isConnected: true));
        Assert.False(handler.CanUpload(row, isConnected: true));
    }

    [Fact]
    public void WhenMissingInAccAndConnectedThenCanUpload()
    {
        var handler = CreateHandlerWithCoordinator();
        var row = RowWithAttachments() with
        {
            AccProcessingStatus = EmailAccProcessingStatus.MissingInAcc,
            AccStatusDisplay = "חסר ב-ACC",
        };

        Assert.True(handler.CanUpload(row, isConnected: true));
        Assert.Equal("הפעולה אינה זמינה.", handler.DescribeUploadDisabledReason(row, isConnected: true));
    }

    [Fact]
    public void WhenNotConnectedThenDescribeUploadDisabledReasonAsksForGmail()
    {
        var handler = CreateHandlerWithCoordinator();

        Assert.Equal("התחבר ל-Gmail.", handler.DescribeUploadDisabledReason(RowWithAttachments(), isConnected: false));
        Assert.False(handler.CanUpload(RowWithAttachments(), isConnected: false));
    }

    private static EmailAccSelectionHandler CreateHandlerWithCoordinator()
    {
        var coordinator = new Mock<IEmailAccUploadCoordinator>();
        return new EmailAccSelectionHandler(
            statusService: null,
            uploadCoordinator: coordinator.Object);
    }

    private static EmailListRow RowWithAttachments() =>
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
            AttachmentCount: 2,
            InternetMessageId: "<msg@test.com>");
}
