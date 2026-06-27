using SiNet.LegacyBridge.Email;
using SiOffice.GoogleConnector;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Binds the new <see cref="ILegacyEmailSource"/> strangler seam to the existing, already
/// authenticated <see cref="GoogleService"/> singleton.
/// <para>
/// This is the single place that knows both worlds: it calls the legacy reads and projects the
/// rich legacy <c>EmailInfo</c> model down to the bridge-local <see cref="LegacyEmailDto"/> (no
/// WPF/presentation members cross the boundary). It deliberately swallows the legacy
/// "Not logged in." state into an empty result so the new UI degrades gracefully.
/// </para>
/// </summary>
internal sealed class GoogleServiceLegacyEmailSource : ILegacyEmailSource
{
    private readonly GoogleService _googleService;

    public GoogleServiceLegacyEmailSource(GoogleService googleService)
    {
        _googleService = googleService;
    }

    public async Task<IReadOnlyList<LegacyEmailDto>> GetProjectEmailsAsync(
        string location,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        List<EmailInfo> emails;
        try
        {
            emails = await _googleService
                .GetProjectEmailsAsync(location, projectName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Legacy service throws "Not logged in." when no Gmail credential is present yet.
            return [];
        }

        var result = new List<LegacyEmailDto>(emails.Count);
        foreach (var email in emails)
        {
            result.Add(ToDto(email));
        }

        return result;
    }

    public async Task<LegacyEmailDto?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var email = await _googleService
                .LoadFullEmailBodyAsync(messageId, cancellationToken)
                .ConfigureAwait(false);

            return email is null ? null : ToDto(email);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static LegacyEmailDto ToDto(EmailInfo email) => new(
        email.MessageId,
        email.ThreadId,
        email.From,
        email.Subject,
        ToReceivedAt(email),
        email.HasAttachments);

    private static DateTimeOffset ToReceivedAt(EmailInfo email)
    {
        var parsed = email.ParsedDate;
        return parsed == DateTime.MinValue
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Local));
    }
}
