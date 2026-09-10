namespace SiNet.Application.Billing;

/// <summary>In-memory preparation orchestrator used by unit tests and as the production algorithm.</summary>
public sealed class BillingPreparationWorkflow
{
    public static BillingPreparationStatus InitialStatus(bool snapshotAvailable) =>
        snapshotAvailable
            ? BillingPreparationStatus.WaitingForSelection
            : BillingPreparationStatus.WaitingForSnapshot;

    public static BillingPreparationStatus AfterSelection(
        IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
        IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
        bool manualOverride)
    {
        if (manualOverride)
            return BillingPreparationStatus.ReadyForApproval;
        if (stages.Count == 0 && hours.Count == 0)
            return BillingPreparationStatus.WaitingForSelection;
        return BillingPreparationStatus.ReadyForApproval;
    }

    public static bool AllRequiredComponentsConfirmed(BillingPreparationRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ManualOverride)
            return request.Hours.All(h => h.ConfirmationMode == BillingConfirmationMode.Manual)
                   && (request.Hours.Count > 0 || request.Stages.Count == 0 || request.Stages.All(s => s.ConfirmationMode != BillingConfirmationMode.None));

        var stagesOk = request.Stages.Count == 0
            || request.Stages.All(s => s.ConfirmationMode != BillingConfirmationMode.None);
        var hoursOk = request.Hours.Count == 0
            || request.Hours.All(h => h.ConfirmationMode == BillingConfirmationMode.Manual);
        return stagesOk && hoursOk && (request.Stages.Count > 0 || request.Hours.Count > 0 || request.ManualOverride);
    }
}
