using System;
using System.Globalization;
using System.Windows.Data;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Converters;

/// <summary>
/// Converts Workflow enum values to their Hebrew display names.
/// Used in ComboBoxes via DisplayMemberPath or cell templates.
/// </summary>
public class WorkflowEnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            // Trigger types
            WorkflowTransitionTriggerType.Manual => "ידני (Manual)",
            WorkflowTransitionTriggerType.AllRequiredTasksClosed => "כל המשימות הושלמו",
            WorkflowTransitionTriggerType.TaskStatusChanged => "סטטוס משימה השתנה",
            WorkflowTransitionTriggerType.SubWorkflowCompleted => "תת-תהליך הסתיים",
            WorkflowTransitionTriggerType.TimerElapsed => "טיימר",

            // Condition types
            WorkflowTransitionConditionType.Always => "תמיד (Always)",
            WorkflowTransitionConditionType.AllTasksComplete => "כל המשימות הושלמו",
            WorkflowTransitionConditionType.TaskStatusEquals => "סטטוס משימה שווה",
            WorkflowTransitionConditionType.TaskStatusNotEquals => "סטטוס משימה לא שווה",
            WorkflowTransitionConditionType.SubWorkflowSucceeded => "תת-תהליך הצליח",
            WorkflowTransitionConditionType.SubWorkflowFailed => "תת-תהליך נכשל",

            // Evaluation modes
            WorkflowEvaluationMode.Auto => "אוטומטי (Auto)",
            WorkflowEvaluationMode.Manual => "ידני (Manual)",
            WorkflowEvaluationMode.AutoWithConfirm => "אוטומטי + אישור",

            // Action types
            WorkflowTransitionActionType.CreateStageTasks => "צור משימות שלב",
            WorkflowTransitionActionType.ClosePreviousStageTasks => "סגור משימות שלב קודם",
            WorkflowTransitionActionType.SendNotification => "שלח התראה",
            WorkflowTransitionActionType.StartSubWorkflow => "הפעל תת-תהליך",
            WorkflowTransitionActionType.SetProjectStatus => "עדכן סטטוס פרויקט",
            WorkflowTransitionActionType.RecordTaskResult => "רשום תוצאת משימה",
            WorkflowTransitionActionType.SetBillingPending => "ממתין לחיוב",
            WorkflowTransitionActionType.CloseProject => "סגור פרויקט",

            // SubWorkflow wait modes
            WorkflowSubWorkflowWaitMode.WaitForCompletion => "המתן לסיום",
            WorkflowSubWorkflowWaitMode.FireAndForget => "הפעל והמשך",

            // Start trigger sources
            WorkflowStartTriggerSource.ManualStart => "🖱️ הפעלה ידנית",
            WorkflowStartTriggerSource.ProjectCreated => "📁 פרויקט נוצר",
            WorkflowStartTriggerSource.ProjectTypeAssigned => "📋 סוג פרויקט הוגדר",
            WorkflowStartTriggerSource.EmailFiled => "📧 תיוק מייל",
            WorkflowStartTriggerSource.ParentWorkflow => "🔄 תהליך אב (SubWorkflow)",
            WorkflowStartTriggerSource.ScheduledTimer => "⏰ טיימר מתוזמן",
            WorkflowStartTriggerSource.ApiCall => "🔌 קריאת API",

            // Email file types
            EmailFileType.General => "📨 תכתובת כללית",
            EmailFileType.WorkOrder => "🏗️ הזמנת עבודה",
            EmailFileType.PlanningMaterial => "📐 חומר לתכנון",
            EmailFileType.Proposal => "💰 הצעת מחיר",
            EmailFileType.ChangeRequest => "📋 בקשת שינוי",
            EmailFileType.Approval => "✅ אישור / אשרור",
            EmailFileType.Report => "📊 דוח / סיכום",

            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
