using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;

namespace SiNet.Infrastructure.Sql.Services.Email;

public sealed class SqlEmailStatusService(IEmailGmailModifyService gmailModify) : IEmailStatusService
{
    private readonly IEmailGmailModifyService _gmailModify =
        gmailModify ?? throw new ArgumentNullException(nameof(gmailModify));

    public async Task<EmailStatusResult> SetStatusAsync(
        SetEmailStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.GmailMessageId))
        {
            return new EmailStatusResult(false, "Missing Gmail message id.");
        }

        try
        {
            await _gmailModify
                .ApplyTriageStatusLabelAsync(command.GmailMessageId, command.Status, cancellationToken)
                .ConfigureAwait(false);
            return new EmailStatusResult(true);
        }
        catch (Exception ex)
        {
            return new EmailStatusResult(false, ex.Message);
        }
    }
}
