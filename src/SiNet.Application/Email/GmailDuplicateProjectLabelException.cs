namespace SiNet.Application.Email;

/// <summary>
/// More than one Gmail project label exists for the same ProjectNumber in this mailbox.
/// </summary>
public sealed class GmailDuplicateProjectLabelException : InvalidOperationException
{
    public GmailDuplicateProjectLabelException(
        int projectNumber,
        IReadOnlyList<ProjectLabelEntry> matches)
        : base(FormatMessage(projectNumber))
    {
        ProjectNumber = projectNumber;
        Matches = matches ?? throw new ArgumentNullException(nameof(matches));
    }

    public int ProjectNumber { get; }

    public IReadOnlyList<ProjectLabelEntry> Matches { get; }

    public static string FormatMessage(int projectNumber)
        => $"קיימות מספר תוויות Gmail לפרויקט {projectNumber}.{Environment.NewLine}יש להסדיר אותן בחלון ניהול התוויות.";
}
