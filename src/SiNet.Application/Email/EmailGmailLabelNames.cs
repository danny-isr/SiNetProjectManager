namespace SiNet.Application.Email;

/// <summary>Gmail label names used for project filing and triage (legacy parity).</summary>
public static class EmailGmailLabelNames
{
    public const string RootLabel = "פרויקטים_משרד";
    public const string Pending = "OfficeSystem_Pending";
    public const string Personal = "OfficeSystem_Personal";
    public const string Irrelevant = "OfficeSystem_Irrelevant";
    public const string Fyi = "OfficeSystem_Fyi";

    public static bool IsProjectLabel(string labelName, string rootLabel = RootLabel) =>
        labelName.StartsWith($"{rootLabel}/", StringComparison.OrdinalIgnoreCase)
            && labelName.Count(static ch => ch == '/') >= 2;

    public static string? FindProjectLabelPath(IEnumerable<string>? labelNames, string rootLabel = RootLabel) =>
        labelNames?.FirstOrDefault(label => IsProjectLabel(label, rootLabel));
}
