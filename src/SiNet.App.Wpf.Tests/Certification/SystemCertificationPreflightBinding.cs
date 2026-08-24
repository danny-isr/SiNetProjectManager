using System.Globalization;
using System.Text.Json;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Fields persisted in a preflight evidence JSON and re-verified before PRP live writes.
/// </summary>
internal sealed record SystemCertificationPreflightBinding(
    string Verdict,
    DateTimeOffset StartedAt,
    string CommitSha,
    string SqlServer,
    string SqlDatabase,
    string WindowsIdentity,
    int OperatorUserId,
    string DatabaseMarker,
    string? GmailExpectedAccount,
    string? AccPlace,
    string? AccInboxProject)
{
    public const string FactCommitSha = "CommitSha";
    public const string FactSqlServer = "SqlServer";
    public const string FactSqlDatabase = "SqlDatabase";
    public const string FactWindowsIdentity = "WindowsIdentity";
    public const string FactOperatorUserId = "OperatorUserId";
    public const string FactDatabaseMarker = "DatabaseMarker";
    public const string FactGmailExpectedAccount = "GmailExpectedAccount";
    public const string FactAccPlace = "AccPlace";
    public const string FactAccInboxProject = "AccInboxProject";

    /// <summary>Maximum age of a preflight report that PRP live may trust.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(8);

    public static bool TryParse(JsonDocument document, out SystemCertificationPreflightBinding? binding, out string? error)
    {
        binding = null;
        error = null;

        if (!document.RootElement.TryGetProperty("Verdict", out var verdictElement))
        {
            error = "Preflight evidence JSON does not contain a Verdict property.";
            return false;
        }

        var verdict = verdictElement.GetString();
        if (!string.Equals(verdict, SystemCertificationEvidence.CertifiedVerdict, StringComparison.Ordinal))
        {
            error = $"Preflight evidence verdict is '{verdict ?? "<null>"}', not "
                    + $"'{SystemCertificationEvidence.CertifiedVerdict}'.";
            return false;
        }

        if (!document.RootElement.TryGetProperty("StartedAt", out var startedAtElement)
            || !TryReadDateTimeOffset(startedAtElement, out var startedAt))
        {
            error = "Preflight evidence JSON must contain StartedAt as an ISO-8601 timestamp.";
            return false;
        }

        if (!TryReadFact(document, FactCommitSha, out var commitSha))
        {
            error = $"Preflight evidence JSON must contain Facts.{FactCommitSha}.";
            return false;
        }

        if (!TryReadFact(document, FactSqlServer, out var sqlServer)
            || !TryReadFact(document, FactSqlDatabase, out var sqlDatabase)
            || !TryReadFact(document, FactWindowsIdentity, out var windowsIdentity)
            || !TryReadFact(document, FactDatabaseMarker, out var databaseMarker))
        {
            error = "Preflight evidence JSON is missing one or more required target Facts.";
            return false;
        }

        if (!TryReadFact(document, FactOperatorUserId, out var operatorRaw)
            || !int.TryParse(operatorRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var operatorUserId)
            || operatorUserId <= 0)
        {
            error = $"Preflight evidence JSON must contain Facts.{FactOperatorUserId} as a positive integer.";
            return false;
        }

        TryReadOptionalFact(document, FactGmailExpectedAccount, out var gmailAccount);
        TryReadOptionalFact(document, FactAccPlace, out var accPlace);
        TryReadOptionalFact(document, FactAccInboxProject, out var accInboxProject);

        binding = new SystemCertificationPreflightBinding(
            verdict!,
            startedAt,
            commitSha!,
            sqlServer!,
            sqlDatabase!,
            windowsIdentity!,
            operatorUserId,
            databaseMarker!,
            gmailAccount,
            accPlace,
            accInboxProject);
        return true;
    }

    public string? ValidateAgainstCurrentRuntime(
        SystemCertificationEnvironment.Target target,
        SystemCertificationEnvironment.GmailLayer gmail,
        SystemCertificationEnvironment.AccLayer acc,
        string? currentCommitSha)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (DateTimeOffset.Now - StartedAt > MaxAge)
        {
            return $"Preflight evidence StartedAt {StartedAt:O} is older than {MaxAge.TotalHours:0} hours.";
        }

        if (string.IsNullOrWhiteSpace(currentCommitSha))
        {
            return "Current commit SHA could not be resolved; PRP live refuses to trust unbound evidence.";
        }

        if (!string.Equals(CommitSha, currentCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            return $"Preflight evidence CommitSha '{CommitSha}' does not match current HEAD '{currentCommitSha}'.";
        }

        if (!string.Equals(SqlServer, target.ServerName, StringComparison.OrdinalIgnoreCase))
        {
            return $"Preflight SqlServer '{SqlServer}' != current '{target.ServerName}'.";
        }

        if (!string.Equals(SqlDatabase, target.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            return $"Preflight SqlDatabase '{SqlDatabase}' != current '{target.DatabaseName}'.";
        }

        if (!string.Equals(WindowsIdentity, target.WindowsIdentityName, StringComparison.OrdinalIgnoreCase))
        {
            return $"Preflight WindowsIdentity '{WindowsIdentity}' != current '{target.WindowsIdentityName}'.";
        }

        if (OperatorUserId != target.OperatorUserId)
        {
            return $"Preflight OperatorUserId {OperatorUserId} != current {target.OperatorUserId}.";
        }

        if (!string.Equals(
                DatabaseMarker,
                SystemCertificationDatabaseMarker.RequiredValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"Preflight DatabaseMarker '{DatabaseMarker}' != required "
                   + $"'{SystemCertificationDatabaseMarker.RequiredValue}'.";
        }

        if (SystemCertificationEnvironment.IsLayerRequested(SystemCertificationEnvironment.GmailEnabledEnv))
        {
            if (gmail.Violation is not null)
            {
                return $"Current Gmail layer is invalid: {gmail.Violation}";
            }

            if (!string.Equals(GmailExpectedAccount, gmail.ExpectedAccount, StringComparison.OrdinalIgnoreCase))
            {
                return $"Preflight GmailExpectedAccount '{GmailExpectedAccount ?? "<null>"}' != current "
                       + $"'{gmail.ExpectedAccount ?? "<null>"}'.";
            }
        }

        if (SystemCertificationEnvironment.IsLayerRequested(SystemCertificationEnvironment.AccEnabledEnv))
        {
            if (acc.Violation is not null)
            {
                return $"Current ACC layer is invalid: {acc.Violation}";
            }

            if (!string.Equals(AccPlace, acc.PlaceTitle, StringComparison.Ordinal))
            {
                return $"Preflight AccPlace '{AccPlace ?? "<null>"}' != current '{acc.PlaceTitle ?? "<null>"}'.";
            }

            if (!string.Equals(AccInboxProject, acc.InboxProjectName, StringComparison.Ordinal))
            {
                return $"Preflight AccInboxProject '{AccInboxProject ?? "<null>"}' != current "
                       + $"'{acc.InboxProjectName ?? "<null>"}'.";
            }
        }

        return null;
    }

    private static bool TryReadFact(JsonDocument document, string name, out string? value)
    {
        value = null;
        if (!document.RootElement.TryGetProperty("Facts", out var facts)
            || !facts.TryGetProperty(name, out var element))
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadOptionalFact(JsonDocument document, string name, out string? value)
    {
        value = null;
        if (!document.RootElement.TryGetProperty("Facts", out var facts)
            || !facts.TryGetProperty(name, out var element))
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static bool TryReadDateTimeOffset(JsonElement element, out DateTimeOffset value)
    {
        if (element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
