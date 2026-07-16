namespace SiNet.Application.Email.Detail;

/// <summary>
/// Builds suggested email actions from workflow context.
/// Unassigned set mirrors legacy SuggestedActionsBuilder.AddUnassociatedActions.
/// </summary>
public static class EmailSuggestedActionsBuilder
{
    public static IReadOnlyList<EmailSuggestedActionDto> Build(EmailWorkflowContextDto context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.HasContext)
        {
            return Array.Empty<EmailSuggestedActionDto>();
        }

        return context.IsAssociatedToProject
            ? BuildAssociated(context)
            : BuildUnassigned(context);
    }

    private static IReadOnlyList<EmailSuggestedActionDto> BuildUnassigned(EmailWorkflowContextDto context)
    {
        var actions = new List<EmailSuggestedActionDto>
        {
            new(
                EmailSuggestedActionCodes.AssociateToExistingProject,
                "שיוך לפרויקט קיים",
                "קישור המייל לפרויקט שכבר קיים במערכת",
                SortOrder: 10),
            new(
                EmailSuggestedActionCodes.CreatePriceQuote,
                "פתיחת הצעת מחיר",
                "מאשר שזה בקשת הצעת מחיר ומתחיל את התהליך (מתקדם לשלב הבא)",
                SortOrder: 20),
            new(
                EmailSuggestedActionCodes.RejectPriceQuote,
                "לא בקשת הצעת מחיר",
                "סוגר את הפנייה כלא רלוונטית להצעת מחיר — בלי דיאלוג נוסף",
                SortOrder: 25),
            new(
                EmailSuggestedActionCodes.CreateNewReview,
                "פתיחת עבודה (מהרשות)",
                "יצירת משימה לפתיחת פרויקט בדיקה על בסיס מייל רשמי מהרשות",
                SortOrder: 30),
            new(
                EmailSuggestedActionCodes.RequestAuthorityInvitation,
                "בקשת הזמנה מהרשות (ממתכנן)",
                "מייל ממתכנן — פתיחת משימת מעקב עד לקבלת הזמנה רשמית מהרשות",
                SortOrder: 40),
            new(
                EmailSuggestedActionCodes.CreateOpinionProject,
                "פתיחת חוות דעת",
                null,
                SortOrder: 50),
        };

        if (context.AttachmentCount > 0)
        {
            actions.Add(new EmailSuggestedActionDto(
                EmailSuggestedActionCodes.CollectMaterial,
                "איסוף חומר",
                $"{context.AttachmentCount} קבצים מצורפים",
                SortOrder: 60));
        }

        actions.Add(new EmailSuggestedActionDto(
            EmailSuggestedActionCodes.ForwardToDecision,
            "העברה להחלטה",
            null,
            SortOrder: 70));

        actions.Add(new EmailSuggestedActionDto(
            EmailSuggestedActionCodes.FileOnly,
            "תיוק בלבד",
            "שמירת המייל ללא פעולה נוספת",
            SortOrder: 80));

        return actions;
    }

    private static IReadOnlyList<EmailSuggestedActionDto> BuildAssociated(EmailWorkflowContextDto context)
    {
        var actions = new List<EmailSuggestedActionDto>();

        if (context.AttachmentCount > 0)
        {
            actions.Add(new EmailSuggestedActionDto(
                Actions.ProcessActionCodes.SendNotification,
                "שלח התראה",
                "הודעה לצוות על מייל עם קבצים מצורפים",
                SortOrder: 10));
        }

        if (context.ActiveWorkflowCount > 0)
        {
            actions.Add(new EmailSuggestedActionDto(
                Actions.ProcessActionCodes.RecordTaskResult,
                "רשום תוצאת משימה",
                "עדכון סטטוס משימה קשורה",
                SortOrder: 20));
        }

        actions.Add(new EmailSuggestedActionDto(
            Actions.ProcessActionCodes.SetProjectStatus,
            "עדכן סטטוס פרויקט",
            "שינוי סטטוס פרויקט לאחר טיפול במייל",
            SortOrder: 30));

        return actions;
    }
}
