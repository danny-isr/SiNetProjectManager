using System.Text;
using SiNet.Application.Abstractions.Email;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

/// <summary>
/// Deterministic, offline tests for the native Gmail MIME builder (<see cref="GmailEmailSender.BuildRawMessage"/>)
/// and its encoding helpers. No Gmail API, OAuth, or network access is involved: the builder is a pure
/// function over an <see cref="EmailSendRequest"/>, so its base64url output is decoded back to the raw
/// RFC 5322 message and asserted directly. A fixed multipart boundary is injected for stable comparisons.
/// </summary>
public sealed class GmailMimeBuilderTests
{
    private const string FixedBoundary = "==SiNet_TEST_BOUNDARY==";

    /// <summary>Reverses Gmail base64url (RFC 4648 §5, unpadded) back to the raw MIME string.</summary>
    private static string DecodeRaw(string base64Url)
    {
        var standard = base64Url.Replace('-', '+').Replace('_', '/');
        switch (standard.Length % 4)
        {
            case 2: standard += "=="; break;
            case 3: standard += "="; break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(standard));
    }

    [Fact]
    public void BuildRawMessage_PlainText_ProducesRfc5322HeadersAndBase64Body()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hello",
            Body = "Hello world",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("To: alice@example.com\r\n", raw);
        Assert.Contains("Subject: Hello\r\n", raw);
        Assert.Contains("MIME-Version: 1.0\r\n", raw);
        Assert.Contains("Content-Type: text/plain; charset=\"UTF-8\"\r\n", raw);
        Assert.Contains("Content-Transfer-Encoding: base64\r\n\r\n", raw);

        var expectedBody = Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello world"));
        Assert.EndsWith(expectedBody, raw);
    }

    [Fact]
    public void BuildRawMessage_NoFrom_OmitsFromHeader()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = "x",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.DoesNotContain("From:", raw);
    }

    [Fact]
    public void BuildRawMessage_WithFrom_EmitsFromHeader()
    {
        var request = new EmailSendRequest
        {
            From = "sender@example.com",
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = "x",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("From: sender@example.com\r\n", raw);
    }

    [Fact]
    public void BuildRawMessage_HtmlBody_UsesTextHtmlContentType()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = "<p>hi</p>",
            IsHtml = true,
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("Content-Type: text/html; charset=\"UTF-8\"\r\n", raw);
    }

    [Fact]
    public void BuildRawMessage_MultipleRecipients_EmitsCcAndBcc()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "a@example.com", "b@example.com" },
            Cc = new[] { "c@example.com" },
            Bcc = new[] { "d@example.com" },
            Subject = "Hi",
            Body = "x",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("To: a@example.com, b@example.com\r\n", raw);
        Assert.Contains("Cc: c@example.com\r\n", raw);
        Assert.Contains("Bcc: d@example.com\r\n", raw);
    }

    [Fact]
    public void BuildRawMessage_NonAsciiSubject_UsesRfc2047EncodedWord()
    {
        const string subject = "שלום עולם";
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = subject,
            Body = "x",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        var expected = "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(subject)) + "?=";
        Assert.Contains("Subject: " + expected + "\r\n", raw);
    }

    [Fact]
    public void BuildRawMessage_AsciiSubject_IsNotEncoded()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Plain ASCII",
            Body = "x",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("Subject: Plain ASCII\r\n", raw);
        Assert.DoesNotContain("=?UTF-8?B?", raw);
    }

    [Fact]
    public void BuildRawMessage_NonAsciiBody_IsBase64Encoded()
    {
        const string body = "תוכן בעברית";
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = body,
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        var expectedBody = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        Assert.EndsWith(expectedBody, raw);
    }

    [Fact]
    public void BuildRawMessage_WithInReplyTo_EmitsThreadingHeaders()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Re: Hi",
            Body = "x",
            InReplyToMessageId = "abc123@mail.example.com",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("In-Reply-To: <abc123@mail.example.com>\r\n", raw);
        Assert.Contains("References: <abc123@mail.example.com>\r\n", raw);
    }

    [Fact]
    public void BuildRawMessage_InReplyToAlreadyBracketed_IsNotDoubleWrapped()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Re: Hi",
            Body = "x",
            InReplyToMessageId = "<abc123@mail.example.com>",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("In-Reply-To: <abc123@mail.example.com>\r\n", raw);
        Assert.DoesNotContain("<<", raw);
        Assert.DoesNotContain(">>", raw);
    }

    [Fact]
    public void BuildRawMessage_NoInReplyTo_OmitsThreadingHeaders()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = "x",
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.DoesNotContain("In-Reply-To:", raw);
        Assert.DoesNotContain("References:", raw);
    }

    [Fact]
    public void BuildRawMessage_WithAttachment_ProducesMultipartMixed()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "With file",
            Body = "see attached",
            Attachments = new[]
            {
                new EmailAttachment("quote.pdf", "application/pdf", Encoding.UTF8.GetBytes("PDFDATA")),
            },
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request, FixedBoundary));

        Assert.Contains("Content-Type: multipart/mixed; boundary=\"" + FixedBoundary + "\"\r\n", raw);
        Assert.Contains("--" + FixedBoundary + "\r\n", raw);
        Assert.Contains("Content-Type: text/plain; charset=\"UTF-8\"\r\n", raw);
        Assert.Contains("Content-Type: application/pdf; name=\"quote.pdf\"\r\n", raw);
        Assert.Contains("Content-Disposition: attachment; filename=\"quote.pdf\"\r\n", raw);
        Assert.EndsWith("--" + FixedBoundary + "--", raw);

        var expectedAttachment = Convert.ToBase64String(Encoding.UTF8.GetBytes("PDFDATA"));
        Assert.Contains(expectedAttachment, raw);
    }

    [Fact]
    public void BuildRawMessage_AttachmentWithoutContentType_FallsBackToOctetStream()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "With file",
            Body = "x",
            Attachments = new[]
            {
                new EmailAttachment("data.bin", null, new byte[] { 1, 2, 3 }),
            },
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request, FixedBoundary));

        Assert.Contains("Content-Type: application/octet-stream; name=\"data.bin\"\r\n", raw);
    }

    [Fact]
    public void BuildRawMessage_AttachmentWithNonAsciiFileName_EncodesFileName()
    {
        const string fileName = "דוח.pdf";
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "With file",
            Body = "x",
            Attachments = new[]
            {
                new EmailAttachment(fileName, "application/pdf", new byte[] { 9 }),
            },
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request, FixedBoundary));

        var encodedName = "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName)) + "?=";
        Assert.Contains("name=\"" + encodedName + "\"", raw);
        Assert.Contains("filename=\"" + encodedName + "\"", raw);
    }

    [Fact]
    public void BuildRawMessage_MultipleAttachments_EmitsBothParts()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Two files",
            Body = "x",
            Attachments = new[]
            {
                new EmailAttachment("a.txt", "text/plain", Encoding.UTF8.GetBytes("AAA")),
                new EmailAttachment("b.txt", "text/plain", Encoding.UTF8.GetBytes("BBB")),
            },
        };

        var raw = DecodeRaw(GmailEmailSender.BuildRawMessage(request, FixedBoundary));

        Assert.Contains("name=\"a.txt\"", raw);
        Assert.Contains("name=\"b.txt\"", raw);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("AAA")), raw);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("BBB")), raw);

        // Two attachment parts + one body part => three opening boundary markers.
        var openings = CountOccurrences(raw, "--" + FixedBoundary + "\r\n");
        Assert.Equal(3, openings);
    }

    [Fact]
    public void BuildRawMessage_RandomBoundary_IsUsedWhenNotInjected()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = "x",
            Attachments = new[]
            {
                new EmailAttachment("a.txt", "text/plain", new byte[] { 1 }),
            },
        };

        var raw1 = DecodeRaw(GmailEmailSender.BuildRawMessage(request));
        var raw2 = DecodeRaw(GmailEmailSender.BuildRawMessage(request));

        Assert.Contains("boundary=\"==SiNet_", raw1);
        Assert.NotEqual(raw1, raw2); // random boundary per call
    }

    [Fact]
    public void BuildRawMessage_OutputIsValidBase64Url_NoPaddingOrUnsafeChars()
    {
        var request = new EmailSendRequest
        {
            To = new[] { "alice@example.com" },
            Subject = "Hi",
            Body = "x",
        };

        var raw = GmailEmailSender.BuildRawMessage(request);

        Assert.DoesNotContain("=", raw);
        Assert.DoesNotContain("+", raw);
        Assert.DoesNotContain("/", raw);
        // Round-trips cleanly back to a non-empty MIME string.
        Assert.False(string.IsNullOrEmpty(DecodeRaw(raw)));
    }

    [Theory]
    [InlineData("danny@example.com", true)]
    [InlineData("Danny <danny@example.com>", true)]
    [InlineData("דני <danny@example.com>", false)]
    public void IsAscii_DetectsNonAsciiContent(string value, bool expected)
    {
        Assert.Equal(expected, GmailEmailSender.IsAscii(value));
    }

    [Fact]
    public void EncodeHeader_AsciiValue_IsUnchanged()
    {
        Assert.Equal("Danny <danny@example.com>", GmailEmailSender.EncodeHeader("Danny <danny@example.com>"));
    }

    [Fact]
    public void EncodeHeader_NonAsciiValue_IsRfc2047EncodedWord()
    {
        const string value = "שלום";
        var expected = "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
        Assert.Equal(expected, GmailEmailSender.EncodeHeader(value));
    }

    [Fact]
    public void EncodeAddressList_SkipsBlankEntriesAndJoinsWithCommaSpace()
    {
        var result = GmailEmailSender.EncodeAddressList(new[] { "a@example.com", "  ", "b@example.com" });
        Assert.Equal("a@example.com, b@example.com", result);
    }

    [Theory]
    [InlineData("abc", "<abc>")]
    [InlineData("  abc  ", "<abc>")]
    [InlineData("<abc>", "<abc>")]
    [InlineData("<abc", "<abc>")]
    [InlineData("abc>", "<abc>")]
    public void EnsureAngleBrackets_NormalizesMessageId(string input, string expected)
    {
        Assert.Equal(expected, GmailEmailSender.EnsureAngleBrackets(input));
    }

    [Fact]
    public void ChunkBase64_ShortInput_IsReturnedUnchanged()
    {
        const string input = "QUJD"; // <= 76 chars
        Assert.Equal(input, GmailEmailSender.ChunkBase64(input));
    }

    [Fact]
    public void ChunkBase64_LongInput_IsWrappedAt76Characters()
    {
        var input = new string('A', 200);

        var chunked = GmailEmailSender.ChunkBase64(input);

        var lines = chunked.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, line => Assert.True(line.Length <= 76));
        Assert.Equal(input, string.Concat(lines));
        Assert.Equal(new[] { 76, 76, 48 }, lines.Select(l => l.Length).ToArray());
    }

    [Fact]
    public void Base64UrlEncode_ReplacesUnsafeCharsAndStripsPadding()
    {
        // 0xFB 0xFF 0xBF => standard base64 "+/+/" (contains both + and /), padded.
        var bytes = new byte[] { 0xFB, 0xFF, 0xBF };
        var standard = Convert.ToBase64String(bytes); // "+/+/"

        var result = GmailEmailSender.Base64UrlEncode(bytes);

        Assert.DoesNotContain("+", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("=", result);
        Assert.Equal(standard.TrimEnd('=').Replace('+', '-').Replace('/', '_'), result);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
