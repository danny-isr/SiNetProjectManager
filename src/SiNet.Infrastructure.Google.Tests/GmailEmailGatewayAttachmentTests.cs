using Google.Apis.Gmail.v1.Data;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailEmailGatewayAttachmentTests
{
    [Fact]
    public void CountAttachments_includes_real_attachments_and_excludes_inline_images()
    {
        var payload = new MessagePart
        {
            Parts =
            [
                CreateAttachmentPart("report.pdf", "att-real"),
                CreateInlineImagePart("logo.png", "att-inline"),
            ],
        };

        Assert.Equal(1, GmailEmailGateway.CountAttachments(payload));
    }

    [Fact]
    public void CountAttachments_counts_nested_real_attachments()
    {
        var payload = new MessagePart
        {
            Parts =
            [
                new MessagePart
                {
                    Parts =
                    [
                        CreateAttachmentPart("a.pdf", "att-1"),
                        CreateAttachmentPart("b.docx", "att-2"),
                    ],
                },
            ],
        };

        Assert.Equal(2, GmailEmailGateway.CountAttachments(payload));
    }

    private static MessagePart CreateAttachmentPart(string filename, string attachmentId) =>
        new()
        {
            Filename = filename,
            MimeType = "application/octet-stream",
            Body = new MessagePartBody { AttachmentId = attachmentId },
            Headers =
            [
                new MessagePartHeader
                {
                    Name = "Content-Disposition",
                    Value = $"attachment; filename=\"{filename}\"",
                },
            ],
        };

    private static MessagePart CreateInlineImagePart(string filename, string attachmentId) =>
        new()
        {
            Filename = filename,
            MimeType = "image/png",
            Body = new MessagePartBody { AttachmentId = attachmentId },
            Headers =
            [
                new MessagePartHeader { Name = "Content-Disposition", Value = "inline" },
                new MessagePartHeader { Name = "Content-ID", Value = "<logo001>" },
            ],
        };
}
