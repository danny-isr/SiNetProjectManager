using SiNet.Application.Settings;

namespace SiNet.Application.Workflow;

/// <summary>
/// Fail-closed Controlled Production Pilot evaluation for new <strong>root</strong> workflow starts.
/// Shared by <c>NativeWorkflowCommandService.StartAsync</c> and QuoteApproved pre-validation.
/// </summary>
public static class PilotStartPolicy
{
    /// <summary>
    /// Returns whether <paramref name="userId"/> may start a root workflow with
    /// <paramref name="workflowCode"/> under the given Pilot settings.
    /// </summary>
    public static bool IsRootStartAllowed(
        WorkflowSystemSettingsDto workflow,
        int userId,
        string? workflowCode,
        out string denyReasonHebrew)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        if (!workflow.PilotEnabled)
        {
            denyReasonHebrew =
                "הפעלת תהליך חדש חסומה: מצב הפיילוט כבוי (Pilot.Enabled=false).";
            return false;
        }

        var allowedUsers = ParseIntCsv(workflow.PilotAllowedUserIds);
        if (userId <= 0 || !allowedUsers.Contains(userId))
        {
            denyReasonHebrew =
                $"הפעלת תהליך חדש חסומה: המשתמש {userId} אינו ברשימת הפיילוט (Pilot.AllowedUserIds).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(workflowCode))
        {
            denyReasonHebrew = "הפעלת תהליך חדש חסומה: קוד תהליך חסר.";
            return false;
        }

        var allowedCodes = ParseCodeCsv(workflow.PilotAllowedWorkflowCodes);
        if (!allowedCodes.Contains(workflowCode.Trim()))
        {
            denyReasonHebrew =
                $"הפעלת תהליך חדש חסומה: הקוד «{workflowCode.Trim()}» אינו ברשימת הפיילוט (Pilot.AllowedWorkflowCodes).";
            return false;
        }

        denyReasonHebrew = string.Empty;
        return true;
    }

    public static HashSet<int> ParseIntCsv(string? csv)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(csv))
            return set;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id) && id > 0)
                set.Add(id);
        }

        return set;
    }

    public static HashSet<string> ParseCodeCsv(string? csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv))
            return set;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                set.Add(part);
        }

        return set;
    }
}
