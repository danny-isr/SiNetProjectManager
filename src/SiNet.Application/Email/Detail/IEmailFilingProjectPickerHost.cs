namespace SiNet.Application.Email.Detail;

/// <summary>
/// Opens a project picker for email filing without changing the global current project.
/// </summary>
public interface IEmailFilingProjectPickerHost
{
    bool IsAvailable { get; }

    /// <summary>
    /// Shows a modal project selector. Returns null if the user cancels.
    /// Must not call <c>ICurrentProjectContext.SetCurrentProjectAsync</c>.
    /// </summary>
    Task<Projects.ProjectSummaryDto?> PickProjectAsync(CancellationToken cancellationToken = default);
}
