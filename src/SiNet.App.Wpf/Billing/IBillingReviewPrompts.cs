namespace SiNet.App.Wpf.Billing;

/// <summary>UI prompts for local billing review writes. Tests inject a fake.</summary>
public interface IBillingReviewPrompts
{
    bool ConfirmPrepareBill(string projectLabel);

    BillingNotNowPromptResult? PromptNotNow(string projectLabel);

    bool ConfirmClearDecision(string projectLabel);
}

public sealed record BillingNotNowPromptResult(string Reason, DateTime? ReviewAgainDate);
