using Microsoft.Data.SqlClient;
using System.Security.Principal;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Fail-closed target resolution for the Full System Workflow Certification tier.
/// <para>
/// Certification performs real writes across SQL, Gmail and ACC, so it demands <b>positive</b> proof of
/// the approved DEV target rather than the operator's word for it. Three independent conditions must all
/// hold before the first write (<c>docs/certification/SYSTEM_WORKFLOW_CERTIFICATION_AUDIT.md</c> §4.3):
/// </para>
/// <list type="number">
/// <item>a <c>SystemSettings</c> marker row inside the target database naming it as the approved target —
/// verified separately by <see cref="SystemCertificationDatabaseMarker"/>, because a marker this harness
/// could create would prove nothing;</item>
/// <item>the resolved SQL server <b>and</b> database each appear on an explicit allowlist supplied
/// independently of the connection string;</item>
/// <item>the current Windows identity appears on an explicit allowlist.</item>
/// </list>
/// <para>
/// The Windows identity is a defence in depth only: the same operator can reach a different database, so it
/// is never accepted as evidence of DEV on its own.
/// </para>
/// <para>
/// Two outcomes are deliberately distinguished. When the tier is simply not switched on the run
/// <b>skips</b>, so CI and the offline suite are unaffected. When the tier <i>is</i> switched on but the
/// target cannot be proven approved, that is a <b>violation</b> and the test must fail before writing
/// anything — never a skip, which would look identical to a clean run.
/// </para>
/// </summary>
internal static class SystemCertificationEnvironment
{
    public const string EnabledEnv = "SINET_SYSTEM_CERT";
    public const string SqlConnectionEnv = "SINET_SYSTEM_CERT_SQL";
    public const string AllowedServersEnv = "SINET_SYSTEM_CERT_ALLOWED_SERVERS";
    public const string AllowedDatabasesEnv = "SINET_SYSTEM_CERT_ALLOWED_DATABASES";
    public const string AllowedWindowsUsersEnv = "SINET_SYSTEM_CERT_ALLOWED_WINDOWS_USERS";
    public const string OperatorUserIdEnv = "SINET_SYSTEM_CERT_USER_ID";

    public const string GmailEnabledEnv = "SINET_SYSTEM_CERT_GMAIL";
    public const string GmailAccountEnv = "SINET_SYSTEM_CERT_GMAIL_ACCOUNT";

    public const string AccEnabledEnv = "SINET_SYSTEM_CERT_ACC";
    public const string AccPlaceEnv = "SINET_SYSTEM_CERT_ACC_PLACE";
    public const string AccInboxProjectEnv = "SINET_SYSTEM_CERT_ACC_INBOX_PROJECT";

    /// <summary>The only Place title the ACC layer may target — see <c>docs/ENVIRONMENTS.md</c> §5.1.</summary>
    public const string RequiredAccPlaceTitle = "SI";

    /// <summary>Title prefix for every row the tier creates, so evidence and cleanup are unambiguous.</summary>
    public const string CertificationTitlePrefix = "[SYS-CERT]";

    /// <summary>
    /// Resolution outcome. Exactly one of the three states holds: not enabled (skip), enabled but
    /// unauthorised (<see cref="Violation"/> — fail), or authorised.
    /// </summary>
    internal sealed record Target(
        bool IsEnabled,
        string? SkipReason,
        string? Violation,
        string? ConnectionString,
        string? ServerName,
        string? DatabaseName,
        string? WindowsIdentityName,
        int OperatorUserId)
    {
        public bool IsAuthorised => IsEnabled && Violation is null;
    }

    internal sealed record GmailLayer(
        bool IsEnabled,
        string? SkipReason,
        string? Violation,
        string? ExpectedAccount);

    internal sealed record AccLayer(
        bool IsEnabled,
        string? SkipReason,
        string? Violation,
        string? PlaceTitle,
        string? InboxProjectName);

    /// <summary>True when the operator opted into the certification tier via <see cref="EnabledEnv"/>.</summary>
    public static bool IsCertificationTierRequested() => IsFlagSet(EnabledEnv);

    /// <summary>True when an optional layer flag is set (Gmail or ACC).</summary>
    public static bool IsLayerRequested(string layerEnabledEnv) => IsFlagSet(layerEnabledEnv);

    /// <summary>
    /// Resolves and authorises the SQL target from environment only. Does not touch the database; the
    /// in-database marker is checked by <see cref="SystemCertificationDatabaseMarker"/> once a context
    /// exists, and both must pass before any write.
    /// </summary>
    public static Target TryResolveTarget()
    {
        if (!IsFlagSet(EnabledEnv))
        {
            return NotEnabled(
                $"Set {EnabledEnv}=1 to opt in to the Full System Workflow Certification tier "
                + "(docs/certification/SYSTEM_WORKFLOW_CERTIFICATION_AUDIT.md). This tier writes to SQL, "
                + "Gmail and ACC.");
        }

        var connection = Read(SqlConnectionEnv);
        if (string.IsNullOrWhiteSpace(connection))
        {
            return Violated(
                $"{SqlConnectionEnv} is required. The certification tier never resolves the connection "
                + "string from the vault, because on a PROD machine that is the production database.");
        }

        string server;
        string database;
        try
        {
            var builder = new SqlConnectionStringBuilder(connection);
            server = builder.DataSource;
            database = builder.InitialCatalog;
        }
        catch (ArgumentException ex)
        {
            return Violated($"{SqlConnectionEnv} is not a valid SQL connection string: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            return Violated(
                $"{SqlConnectionEnv} must name both a server (Data Source) and a database (Initial Catalog).");
        }

        var windowsIdentity = WindowsIdentity.GetCurrent().Name;
        var allowedServers = ReadAllowlist(AllowedServersEnv);
        if (allowedServers.Count == 0)
        {
            return Violated(
                $"{AllowedServersEnv} is required: the approved SQL server(s), supplied independently of "
                + "the connection string. Re-stating the connection string proves nothing.",
                server, database, windowsIdentity);
        }

        if (!allowedServers.Contains(server, StringComparer.OrdinalIgnoreCase))
        {
            return Violated(
                $"SQL server '{server}' is not on {AllowedServersEnv} "
                + $"([{string.Join(", ", allowedServers)}]). Refusing to write to an unapproved server.",
                server, database, windowsIdentity);
        }

        var allowedDatabases = ReadAllowlist(AllowedDatabasesEnv);
        if (allowedDatabases.Count == 0)
        {
            return Violated(
                $"{AllowedDatabasesEnv} is required: the approved database name(s).",
                server, database, windowsIdentity);
        }

        if (!allowedDatabases.Contains(database, StringComparer.OrdinalIgnoreCase))
        {
            return Violated(
                $"Database '{database}' is not on {AllowedDatabasesEnv} "
                + $"([{string.Join(", ", allowedDatabases)}]). Refusing to write to an unapproved database.",
                server, database, windowsIdentity);
        }

        var allowedUsers = ReadAllowlist(AllowedWindowsUsersEnv);
        if (allowedUsers.Count == 0)
        {
            return Violated(
                $"{AllowedWindowsUsersEnv} is required: the approved DEV operator Windows identity, "
                + @"for example 'AzureAD\dannyisrael'.",
                server, database, windowsIdentity);
        }

        // Exact match only. The SIUser mechanism this mirrors compares LoginName by equality
        // (PilotSmokeSeed.EnsureOperatorLoginAsync), so partial matching would be both novel and unsafe.
        if (!allowedUsers.Contains(windowsIdentity, StringComparer.OrdinalIgnoreCase))
        {
            return Violated(
                $"Windows identity '{windowsIdentity}' is not on {AllowedWindowsUsersEnv} "
                + $"([{string.Join(", ", allowedUsers)}]). Exact match is required — no partial matching.",
                server, database, windowsIdentity);
        }

        var operatorRaw = Read(OperatorUserIdEnv);
        if (!int.TryParse(operatorRaw, out var operatorUserId) || operatorUserId <= 0)
        {
            return Violated(
                $"{OperatorUserIdEnv} must be the operator's positive SIUser.Id on the target database.",
                server, database, windowsIdentity);
        }

        return new Target(
            IsEnabled: true,
            SkipReason: null,
            Violation: null,
            connection.Trim(),
            server,
            database,
            windowsIdentity,
            operatorUserId);

        static Target NotEnabled(string reason) =>
            new(false, reason, null, null, null, null, null, 0);

        static Target Violated(string violation, string? server = null, string? database = null, string? identity = null) =>
            new(
                true,
                null,
                violation,
                null,
                server,
                database,
                identity ?? WindowsIdentity.GetCurrent().Name,
                0);
    }

    public static GmailLayer TryResolveGmailLayer()
    {
        if (!IsFlagSet(GmailEnabledEnv))
        {
            return new GmailLayer(
                false,
                $"Set {GmailEnabledEnv}=1 to include the Gmail layer.",
                null,
                null);
        }

        var account = Read(GmailAccountEnv);
        if (string.IsNullOrWhiteSpace(account))
        {
            return new GmailLayer(
                true,
                null,
                $"{GmailAccountEnv} is required: the mailbox the stored token must authenticate as. The "
                + @"token under %LOCALAPPDATA%\SiNet\google-token belongs to whichever account last "
                + "consented on this machine, which may not be the intended mailbox.",
                null);
        }

        return new GmailLayer(true, null, null, account.Trim());
    }

    public static AccLayer TryResolveAccLayer(GmailLayer gmail)
    {
        ArgumentNullException.ThrowIfNull(gmail);

        if (!IsFlagSet(AccEnabledEnv))
        {
            return new AccLayer(
                false,
                $"Set {AccEnabledEnv}=1 to include the ACC layer.",
                null,
                null,
                null);
        }

        if (!gmail.IsEnabled || gmail.Violation is not null)
        {
            var reason = gmail.Violation
                           ?? gmail.SkipReason
                           ?? $"Set {GmailEnabledEnv}=1 before enabling ACC.";
            return new AccLayer(
                true,
                null,
                $"The ACC layer requires a valid Gmail layer ({GmailEnabledEnv}=1): {reason}",
                null,
                null);
        }

        var place = Read(AccPlaceEnv);
        if (!string.Equals(place, RequiredAccPlaceTitle, StringComparison.Ordinal))
        {
            return new AccLayer(
                true,
                null,
                $"{AccPlaceEnv} must be exactly '{RequiredAccPlaceTitle}' (docs/ENVIRONMENTS.md §5.1). "
                + $"Got '{place ?? "<null>"}'.",
                null,
                null);
        }

        var inboxProject = Read(AccInboxProjectEnv);
        if (string.IsNullOrWhiteSpace(inboxProject))
        {
            return new AccLayer(
                true,
                null,
                $"{AccInboxProjectEnv} is required: the disposable ACC project written temporarily into "
                + "InboxProjectName. Without it, ingest targets the office Inbox project named by the "
                + "restored database (docs/ENVIRONMENTS.md §5.1.1).",
                null,
                null);
        }

        return new AccLayer(true, null, null, place, inboxProject.Trim());
    }

    private static bool IsFlagSet(string name)
    {
        var value = Read(name);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ReadAllowlist(string name) =>
        (Read(name) ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
