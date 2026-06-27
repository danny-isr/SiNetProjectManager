namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Manages Gmail labels. Labels represent responsible-assignee routing only and must
/// never encode statuses.
/// </summary>
public interface IEmailLabelService
{
    Task<IReadOnlyList<string>> GetLabelsAsync(CancellationToken cancellationToken = default);

    Task ApplyLabelAsync(string messageId, string label, CancellationToken cancellationToken = default);

    Task RemoveLabelAsync(string messageId, string label, CancellationToken cancellationToken = default);
}
