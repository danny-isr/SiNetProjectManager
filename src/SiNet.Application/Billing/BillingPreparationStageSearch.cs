namespace SiNet.Application.Billing;

/// <summary>
/// UI-only matching for the preparation stage hierarchy. Never changes
/// Included, AdditionPercent, persistence, or summary math.
/// </summary>
public static class BillingPreparationStageSearch
{
    public static bool IsActive(string? query) => !string.IsNullOrWhiteSpace(query);

    public static bool Matches(string? value, string? query)
    {
        if (!IsActive(query) || string.IsNullOrWhiteSpace(value))
            return false;
        return value.Contains(query!.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    public static bool MatchesContract(string? contractName, string? contractNumber, string? query) =>
        Matches(contractName, query) || Matches(contractNumber, query);

    public static bool MatchesSubContract(string? subContractName, string? subContractNumber, string? query) =>
        Matches(subContractName, query) || Matches(subContractNumber, query);

    public static bool MatchesStageName(string? stageName, string? query) =>
        Matches(stageName, query);
}
