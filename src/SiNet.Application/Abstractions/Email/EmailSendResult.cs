namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Outcome of an <see cref="IEmailSender.SendAsync"/> attempt. Expected failures (not signed in,
/// missing send consent, validation, transient API errors) are reported here rather than thrown,
/// matching the non-throwing convention used across the email ports.
/// </summary>
public sealed record EmailSendResult
{
    /// <summary><c>true</c> when the message was accepted by Gmail.</summary>
    public bool Success { get; init; }

    /// <summary>The id of the sent message when <see cref="Success"/> is <c>true</c>.</summary>
    public string? MessageId { get; init; }

    /// <summary>Human-readable error description when <see cref="Success"/> is <c>false</c>.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// <c>true</c> when sending failed specifically because the current Gmail session lacks the
    /// send scope (the persisted token predates the scope expansion). The caller should trigger a
    /// deliberate, user-initiated interactive sign-in to re-grant read + send, then retry.
    /// </summary>
    public bool RequiresConsent { get; init; }

    /// <summary>
    /// <c>true</c> when the failure is transient (e.g. network / rate limit) and a retry may succeed.
    /// </summary>
    public bool ShouldRetry { get; init; }

    public static EmailSendResult Sent(string messageId) =>
        new() { Success = true, MessageId = messageId };

    public static EmailSendResult Fail(string error, bool shouldRetry = false) =>
        new() { Success = false, Error = error, ShouldRetry = shouldRetry };

    public static EmailSendResult ConsentRequired(string error) =>
        new() { Success = false, Error = error, RequiresConsent = true };
}
