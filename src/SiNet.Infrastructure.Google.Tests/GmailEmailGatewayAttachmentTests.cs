using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Logging;
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

    [Fact]
    public void MapForTests_includes_attachment_count_from_payload_parts()
    {
        var gateway = CreateGateway();
        var message = new Message
        {
            Id = "msg-att",
            ThreadId = "thread-att",
            Snippet = "See attached",
            LabelIds = ["INBOX"],
            Payload = new MessagePart
            {
                Headers =
                [
                    new MessagePartHeader { Name = "From", Value = "a@example.com" },
                    new MessagePartHeader { Name = "Subject", Value = "Attachments" },
                    new MessagePartHeader { Name = "Date", Value = "Mon, 1 Jan 2024 12:00:00 +0000" },
                ],
                Parts =
                [
                    CreateAttachmentPart("report.pdf", "att-real"),
                ],
            },
        };

        var summary = gateway.MapForTests(message);

        Assert.Equal(1, summary.AttachmentCount);
    }

    [Fact]
    public void TryGetSummaryAsync_uses_full_format_with_fields_mask()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.Contains("FormatEnum.Full", source, StringComparison.Ordinal);
        Assert.Contains("SummaryFieldsMask", source, StringComparison.Ordinal);
        Assert.Contains("body(attachmentId)", source, StringComparison.Ordinal);
    }

    private static GmailEmailGateway CreateGateway()
    {
        var options = new GmailOptions { TokenStorePath = Path.GetTempPath() };
        var logger = new TestAppLogger();
        var provider = new GmailClientProvider(options, logger);
        return new GmailEmailGateway(provider, logger);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_LIST_MIGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class TestAppLogger : IAppLogger
    {
        public void Debug(string message) { }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
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
