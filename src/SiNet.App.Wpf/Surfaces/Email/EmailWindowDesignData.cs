using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.App.Wpf.Surfaces.Email;

using SiNet.Application.Abstractions.Email;

/// <summary>
/// Lightweight, read-only presentation records used by <see cref="EmailWindowViewModel"/> to populate
/// the visual clone of the legacy <c>EmailManagementView</c> with fake/design-time data ONLY.
/// <para>
/// These types are deliberately simple presentation rows — they are NOT domain entities, NOT EF models,
/// NOT Gmail/Outlook messages, and carry no behavior. The visual-clone slice uses them so the window can
/// render the same panels (folder groups, email list, selected-email viewer, attachments, status bar)
/// without touching the real database, Gmail/Outlook, the file system, or workflow. Real read-only data
/// will replace them later through clean Application ports.
/// </para>
/// </summary>
internal static class EmailWindowDesignData
{
    /// <summary>Fake folder/status buckets shown in the folder filter (mirrors legacy grouping).</summary>
    public static IReadOnlyList<EmailFolderRow> SampleFolders { get; } =
    [
        new("\u05EA\u05D9\u05D1\u05D4 \u05E0\u05DB\u05E0\u05E1\u05EA", 4),        // Inbox
        new("\u05DE\u05D9\u05D9\u05DC\u05D9\u05DD \u05DC\u05EA\u05D9\u05D5\u05E7", 3),   // Emails to file
        new("\u05DE\u05E9\u05D5\u05D9\u05DB\u05D9\u05DD \u05DC\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8", 2), // Assigned to project
        new("\u05D8\u05D5\u05E4\u05DC\u05D5", 5),                // Handled
    ];

    /// <summary>Fake status filter options (mirrors legacy per-email status semantics).</summary>
    public static IReadOnlyList<string> SampleStatuses { get; } =
    [
        "\u05D4\u05DB\u05DC",              // All
        "\u05DC\u05D0 \u05E0\u05E7\u05E8\u05D0\u05D5",       // Unread
        "\u05DE\u05DE\u05EA\u05D9\u05DF \u05DC\u05D8\u05D9\u05E4\u05D5\u05DC",  // Pending
        "\u05DE\u05E9\u05D5\u05D9\u05DA",          // Assigned
        "\u05DC\u05D0 \u05E8\u05DC\u05D5\u05D5\u05E0\u05D8\u05D9",     // Irrelevant
    ];

    /// <summary>A small set of fake emails for the list area, grouped by their project bucket.</summary>
    public static IReadOnlyList<EmailListRow> SampleEmails { get; } =
    [
        new(
            Id: "5001",
            Sender: "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC <danny@example.com>",
            Subject: "\u05D4\u05D9\u05EA\u05E8 \u05D1\u05E0\u05D9\u05D4 \u2014 \u05E2\u05D3\u05DB\u05D5\u05DF \u05EA\u05D5\u05DB\u05E0\u05D9\u05D5\u05EA",
            Preview: "\u05E9\u05DC\u05D5\u05DD, \u05DE\u05E6\u05D5\u05E8\u05E4\u05D5\u05EA \u05D4\u05EA\u05D5\u05DB\u05E0\u05D9\u05D5\u05EA \u05D4\u05DE\u05E2\u05D5\u05D3\u05DB\u05E0\u05D5\u05EA \u05DC\u05D0\u05D9\u05E9\u05D5\u05E8...",
            ReceivedOn: new DateTime(2026, 6, 21, 9, 14, 0),
            GroupName: "\u05DE\u05D9\u05D9\u05DC\u05D9\u05DD \u05DC\u05EA\u05D9\u05D5\u05E7",
            IsUnread: true,
            IsAssigned: false,
            AssignedProjectName: null,
            AttachmentCount: 2),
        new(
            Id: "5002",
            Sender: "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF <ruth@example.com>",
            Subject: "\u05E9\u05D0\u05DC\u05D4 \u05DC\u05D2\u05D1\u05D9 \u05D2\u05D1\u05D5\u05DC\u05D5\u05EA \u05D4\u05DE\u05D2\u05E8\u05E9",
            Preview: "\u05D4\u05D0\u05DD \u05E7\u05D5 \u05D4\u05D1\u05E0\u05D9\u05DF \u05D4\u05E7\u05D3\u05DE\u05D9 \u05EA\u05D5\u05D0\u05DD \u05D0\u05EA \u05D4\u05EA\u05D1\u05E2?",
            ReceivedOn: new DateTime(2026, 6, 20, 16, 42, 0),
            GroupName: "\u05DE\u05D9\u05D9\u05DC\u05D9\u05DD \u05DC\u05EA\u05D9\u05D5\u05E7",
            IsUnread: true,
            IsAssigned: false,
            AssignedProjectName: null,
            AttachmentCount: 0),
        new(
            Id: "5003",
            Sender: "\u05DE\u05E2\u05D5\u05D3\u05DB\u05DF \u05EA\u05DB\u05E0\u05D5\u05DF <planner@example.com>",
            Subject: "\u05EA\u05D2\u05D5\u05D1\u05EA \u05DE\u05EA\u05DB\u05E0\u05DF \u2014 \u05E1\u05D9\u05DE\u05D5\u05DF \u05DE\u05D9\u05D3\u05D5\u05EA",
            Preview: "\u05DE\u05E6\u05D5\u05E8\u05E3 \u05E7\u05D5\u05D1\u05E5 \u05DE\u05EA\u05D5\u05E7\u05DF. \u05E0\u05D0 \u05DC\u05D1\u05D3\u05D5\u05E7...",
            ReceivedOn: new DateTime(2026, 6, 19, 11, 5, 0),
            GroupName: "\u05DE\u05E9\u05D5\u05D9\u05DB\u05D9\u05DD \u05DC\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8",
            IsUnread: false,
            IsAssigned: true,
            AssignedProjectName: "1042 \u2014 \u05DE\u05D2\u05D3\u05DC\u05D9 \u05D4\u05E6\u05E4\u05D5\u05DF",
            AttachmentCount: 1),
        new(
            Id: "5004",
            Sender: "\u05E2\u05D9\u05E8\u05D9\u05D9\u05EA \u05EA\u05DC \u05D0\u05D1\u05D9\u05D1 <city@example.com>",
            Subject: "\u05D0\u05D9\u05E9\u05D5\u05E8 \u05E8\u05E9\u05D5\u05EA \u2014 \u05EA\u05D9\u05E7 \u05D4\u05D9\u05EA\u05E8",
            Preview: "\u05D4\u05D1\u05E7\u05E9\u05D4 \u05D0\u05D5\u05E9\u05E8\u05D4. \u05DE\u05E1\u05DE\u05DB\u05D9\u05DD \u05DE\u05E6\u05D5\u05E8\u05E4\u05D9\u05DD.",
            ReceivedOn: new DateTime(2026, 6, 18, 8, 30, 0),
            GroupName: "\u05DE\u05E9\u05D5\u05D9\u05DB\u05D9\u05DD \u05DC\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8",
            IsUnread: false,
            IsAssigned: true,
            AssignedProjectName: "1042 \u2014 \u05DE\u05D2\u05D3\u05DC\u05D9 \u05D4\u05E6\u05E4\u05D5\u05DF",
            AttachmentCount: 3),
    ];

    /// <summary>Fake attachments for the currently selected email in the viewer.</summary>
    public static IReadOnlyList<EmailAttachmentRow> SampleAttachments { get; } =
    [
        new("\u05EA\u05D5\u05DB\u05E0\u05D9\u05EA_\u05D0\u05D3\u05E8\u05D9\u05DB\u05DC\u05D9\u05EA.pdf", "PDF", "1.8 MB"),
        new("\u05DE\u05E4\u05DC\u05E1_\u05E7\u05E8\u05E7\u05E2.dwg", "DWG", "740 KB"),
    ];

    /// <summary>Fake plain-text body preview for the selected email.</summary>
    public static string SampleBody { get; } =
        "\u05E9\u05DC\u05D5\u05DD,\n\n" +
        "\u05DE\u05E6\u05D5\u05E8\u05E4\u05D5\u05EA \u05D4\u05EA\u05D5\u05DB\u05E0\u05D9\u05D5\u05EA \u05D4\u05DE\u05E2\u05D5\u05D3\u05DB\u05E0\u05D5\u05EA \u05DC\u05D0\u05D9\u05E9\u05D5\u05E8\u05DB\u05DD. " +
        "\u05E0\u05D0 \u05DC\u05E2\u05D9\u05D9\u05DF \u05D1\u05E4\u05E8\u05D8\u05D9 \u05D4\u05D7\u05D9\u05D1\u05D5\u05E8 \u05D5\u05DC\u05D4\u05E9\u05D9\u05D1 \u05D1\u05D4\u05E7\u05D3\u05DD.\n\n" +
        "(\u05EA\u05D5\u05DB\u05DF \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9, \u05DC\u05DC\u05D0 \u05D8\u05E2\u05D9\u05E0\u05EA \u05D3\u05D5\u05D0\u05E8 \u05D0\u05DE\u05D9\u05EA\u05D9)\n\n" +
        "\u05D1\u05D1\u05E8\u05DB\u05D4,\n\u05D3\u05E0\u05D9";
}

/// <summary>Fake folder/status bucket row (name + unread/count badge). Presentation-only.</summary>
public sealed record EmailFolderRow(string Name, int Count);

/// <summary>
/// Fake email list row mirroring the visual fields of the legacy email item
/// (sender, subject, preview, received date, project group, unread/assigned state, attachment count).
/// Presentation-only; not a Gmail/Outlook message and not an EF entity.
/// </summary>
public sealed record EmailListRow(
    string Id,
    string Sender,
    string Subject,
    string Preview,
    DateTime ReceivedOn,
    string GroupName,
    bool IsUnread,
    bool IsAssigned,
    string? AssignedProjectName,
    int AttachmentCount,
    string? InternetMessageId = null,
    string? ThreadId = null,
    string To = "",
    string Snippet = "",
    string LabelsDisplay = "",
    string? PrimaryLabel = null,
    EmailProjectLinkState ProjectLinkState = EmailProjectLinkState.Unlinked,
    int? ProjectId = null,
    string? ProjectNumber = null,
    string? ProjectName = null,
    string ProjectDisplay = "לא משויך",
    IReadOnlyList<string>? LabelChipNames = null,
    IReadOnlyList<EmailLabelChip>? LabelChips = null,
    int? InboxMessageId = null,
    string? ThreadUniqueId = null,
    bool IsFiledToProject = false,
    bool IsFiledToSameProject = false,
    string? FiledProjectLabelPath = null,
    string? RowBackgroundColor = null,
    bool IsActionBusy = false,
    string? ActionStatusText = null,
    string? ActionErrorText = null,
    EmailAccProcessingStatus AccProcessingStatus = EmailAccProcessingStatus.NotChecked,
    string? AccStatusDisplay = null,
    bool IsAccStatusLoading = false,
    bool IsAccUploadBusy = false,
    string? AccUploadStatusText = null,
    int? LabelProjectId = null,
    string? LabelProjectName = null,
    int? ThreadProjectId = null,
    string? ThreadProjectName = null,
    bool HasThreadHistory = false,
    bool IsProjectMismatch = false,
    bool ShowLinkToThreadButton = false)
{
    public const int MaxVisibleLabelChips = 3;

    public IReadOnlyList<EmailLabelChip> DisplayLabelChips =>
        LabelChips is { Count: > 0 } chips
            ? chips
            : (LabelChipNames ?? []).Select(static name => new EmailLabelChip(name)).ToList();

    public bool HasAnyLabels => DisplayLabelChips.Count > 0;

    public IReadOnlyList<EmailLabelChip> VisibleLabelChips =>
        DisplayLabelChips.Take(MaxVisibleLabelChips).ToList();

    public int ExtraLabelCount => Math.Max(0, DisplayLabelChips.Count - MaxVisibleLabelChips);

    public bool HasExtraLabels => ExtraLabelCount > 0;

    /// <summary>Short received-date text shown on the list row.</summary>
    public string ReceivedDisplay => ReceivedOn.ToString("dd/MM/yyyy HH:mm");

    /// <summary>Attachment indicator text ("\uD83D\uDCCE N") shown only when the email has attachments.</summary>
    public string AttachmentBadge => AttachmentCount > 0 ? $"\uD83D\uDCCE {AttachmentCount}" : string.Empty;

    /// <summary>True when this email carries at least one attachment (drives the badge visibility).</summary>
    public bool HasAttachments => AttachmentCount > 0;

    public bool ShowAccStatus =>
        AccProcessingStatus != EmailAccProcessingStatus.NotChecked
        || IsAccStatusLoading
        || IsAccUploadBusy;

    public string AccStatusBadge =>
        IsAccUploadBusy
            ? AccUploadStatusText ?? "מעלה ל-ACC…"
            : IsAccStatusLoading
                ? "בודק ACC…"
                : AccStatusDisplay ?? string.Empty;

    public bool HasAccStatusBadge => ShowAccStatus && !string.IsNullOrWhiteSpace(AccStatusBadge);

    public bool IsAccLockedByOther =>
        AccProcessingStatus == EmailAccProcessingStatus.LockedByOtherUser;

    /// <summary>
    /// Safe UIA / accessibility name from visible metadata only (no body / preview).
    /// </summary>
    public string AutomationName
    {
        get
        {
            var sender = string.IsNullOrWhiteSpace(Sender) ? "?" : Sender.Trim();
            var subject = string.IsNullOrWhiteSpace(Subject) ? "(ללא נושא)" : Subject.Trim();
            if (subject.Length > 80)
            {
                subject = subject[..80] + "…";
            }

            return $"{sender} — {subject}";
        }
    }

    public override string ToString() => AutomationName;

    public bool IsAccPartialFailure =>
        AccProcessingStatus == EmailAccProcessingStatus.PartiallyUploaded
        || AccProcessingStatus == EmailAccProcessingStatus.MissingInAcc;

    public string ProjectLinkDisplay => ProjectLinkState == EmailProjectLinkState.Linked
        ? "משויך"
        : "לא משויך";

    public bool IsLinked => ProjectLinkState == EmailProjectLinkState.Linked;

    public string LinkedProjectBadge
    {
        get
        {
            if (!IsLinked)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(ProjectNumber) && !string.IsNullOrWhiteSpace(ProjectName))
            {
                // Avoid "1042 — (1042)Name" when ProjectName is already the leaf.
                if (ProjectName.Contains(ProjectNumber, StringComparison.Ordinal))
                    return ProjectName;
                return $"{ProjectNumber} — {ProjectName}";
            }

            return ProjectDisplay;
        }
    }

    public string? ProjectDiagnosticsTooltip
    {
        get
        {
            if (IsLinked && !string.IsNullOrWhiteSpace(FiledProjectLabelPath))
                return $"תווית Gmail: {FiledProjectLabelPath}";
            if (IsLinked && !string.IsNullOrWhiteSpace(ProjectDisplay))
                return $"משויך: {ProjectDisplay}";
            if (!IsLinked && ProjectId is int sqlId)
                return $"לא משויך ב-Gmail (קישור במסד ProjectId: {sqlId})";
            return null;
        }
    }

    public string ThreadLinkButtonText
    {
        get
        {
            if (!HasThreadHistory || string.IsNullOrWhiteSpace(ThreadProjectName))
            {
                return string.Empty;
            }

            return IsProjectMismatch
                ? $"⚠️ העבר לשרשור: {ThreadProjectName}"
                : $"🔗 שייך לשרשור: {ThreadProjectName}";
        }
    }
}

/// <summary>Fake attachment row for the selected-email viewer (name + type + size). Presentation-only.</summary>
public sealed record EmailAttachmentRow(string FileName, string Kind, string Size)
{
    /// <summary>Combined label shown in the attachment chip ("name (type, size)").</summary>
    public string DisplayLabel => $"{FileName}  ({Kind}, {Size})";
}
