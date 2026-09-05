namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Describes a single validation error found during pre-sync template scanning.
/// </summary>
/// <param name="RuleCode">Machine-readable rule identifier (e.g. "MISSING_PAIR", "CHAPTER_MISMATCH").</param>
/// <param name="Message">Human-readable Hebrew description of the error.</param>
/// <param name="SectionCode">Optional section code related to the error.</param>
public sealed record TemplateValidationError(
    string RuleCode,
    string Message,
    string? SectionCode = null);
