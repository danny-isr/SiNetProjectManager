namespace SiNet.Application.Workflow;

/// <summary>
/// Minimal user reference used inside workflow read DTOs (e.g. who performed a transition).
/// </summary>
/// <param name="Id">User identifier.</param>
/// <param name="PersonName">Display name of the user.</param>
public sealed record WorkflowUserRefDto(int Id, string? PersonName);
