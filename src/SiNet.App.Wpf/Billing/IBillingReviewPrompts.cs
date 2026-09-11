namespace SiNet.App.Wpf.Billing;

/// <summary>UI prompts for local billing review writes. Tests inject a fake.</summary>
public interface IBillingReviewPrompts
{
    bool ConfirmPrepareBill(string projectLabel);

    BillingNotNowPromptResult? PromptNotNow(string projectLabel);

    bool ConfirmClearDecision(string projectLabel);

    BillingUnsavedEditsDecision ConfirmDiscardUnsavedPreparationEdits();
}

public enum BillingUnsavedEditsDecision
{
    Stay = 0,
    Discard = 1
}

public sealed record BillingNotNowPromptResult(string Reason, DateTime? ReviewAgainDate);
