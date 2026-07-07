namespace SiNet.Application.Email;

/// <summary>Applies Gmail triage status labels (Pending/Personal/Irrelevant).</summary>
public interface IEmailStatusService
{
    Task<EmailStatusResult> SetStatusAsync(
        SetEmailStatusCommand command,
        CancellationToken cancellationToken = default);
}
