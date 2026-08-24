using Microsoft.Data.SqlClient;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Fail-closed gate resolution for the L4W P0 Pilot write smoke (<c>docs/TEST_STRATEGY.md</c> §4W).
/// <para>
/// Unlike <see cref="LiveEnvironment.TryResolveSqlConnectionString"/> this type <b>never</b> falls
/// back to the vault key <c>SiNet/ConnectionStrings/SiNetDatabase</c>. On a PROD workstation that
/// key is the production database, and a tier that creates projects, workflow instances, Gmail
/// labels and ACC folders must not be able to inherit it implicitly.
/// </para>
/// </summary>
internal static class PilotSmokeEnvironment
{
    public const string EnabledEnv = "SINET_PILOT_SMOKE";
    public const string SqlConnectionEnv = "SINET_PILOT_SMOKE_SQL";
    public const string DatabaseConfirmEnv = "SINET_PILOT_SMOKE_DB_CONFIRM";
    public const string OperatorUserIdEnv = "SINET_PILOT_SMOKE_USER_ID";

    public const string GmailEnabledEnv = "SINET_PILOT_SMOKE_GMAIL";
    public const string GmailSubjectEnv = "SINET_PILOT_SMOKE_GMAIL_SUBJECT";
    public const string GmailAccountEnv = "SINET_PILOT_SMOKE_GMAIL_ACCOUNT";

    public const string AccEnabledEnv = "SINET_PILOT_SMOKE_ACC";
    public const string AccInboxProjectEnv = "SINET_PILOT_SMOKE_ACC_INBOX_PROJECT";
    public const string AccPlaceEnv = "SINET_PILOT_SMOKE_ACC_PLACE";

    /// <summary>The only Place title the ACC tier may target — see <c>docs/ENVIRONMENTS.md</c> §5.1.</summary>
    public const string RequiredAccPlaceTitle = "SI";

    /// <summary>Title prefix for every SQL row this tier creates, so evidence and cleanup are unambiguous.</summary>
    public const string SmokeTitlePrefix = "[P0-SMOKE]";

    internal sealed record SqlTier(
        bool IsEnabled,
        string? SkipReason,
        string? ConnectionString,
        string? DatabaseName,
        string? ServerName,
        int OperatorUserId);

    internal sealed record GmailTier(
        bool IsEnabled,
        string? SkipReason,
        string? SubjectToken,
        string? ExpectedAccount);

    internal sealed record AccTier(
        bool IsEnabled,
        string? SkipReason,
        string? InboxProjectName,
        string? PlaceTitle);

    /// <summary>
    /// Resolves the mandatory SQL tier. Every gate must be present and self-consistent; anything
    /// missing yields <see cref="SqlTier.IsEnabled"/> <see langword="false"/> with a reason so the
    /// test skips rather than fails.
    /// </summary>
    public static SqlTier TryResolveSqlTier()
    {
        if (!LiveFactAttribute.IsLiveEnabled())
        {
            return Disabled($"Set {LiveFactAttribute.EnvVarName}=1 (see docs/TEST_STRATEGY.md §4).");
        }

        if (!IsFlagSet(EnabledEnv))
        {
            return Disabled(
                $"Set {EnabledEnv}=1 to opt in to the P0 Pilot WRITE smoke (see docs/TEST_STRATEGY.md §4W).");
        }

        var connection = Read(SqlConnectionEnv);
        if (string.IsNullOrWhiteSpace(connection))
        {
            return Disabled(
                $"{SqlConnectionEnv} is required. The write tier never resolves the connection string "
                + "from the vault, because on a PROD machine that is the production database.");
        }

        string? database;
        string? server;
        try
        {
            var builder = new SqlConnectionStringBuilder(connection);
            database = builder.InitialCatalog;
            server = builder.DataSource;
        }
        catch (ArgumentException ex)
        {
            return Disabled($"{SqlConnectionEnv} is not a valid SQL connection string: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            return Disabled($"{SqlConnectionEnv} must name a database (Initial Catalog / Database).");
        }

        var confirm = Read(DatabaseConfirmEnv);
        if (string.IsNullOrWhiteSpace(confirm))
        {
            return Disabled(
                $"{DatabaseConfirmEnv} is required: re-enter the database name to confirm the target.");
        }

        if (!string.Equals(confirm, database, StringComparison.OrdinalIgnoreCase))
        {
            return Disabled(
                $"{DatabaseConfirmEnv} ('{confirm}') does not match the database in {SqlConnectionEnv} "
                + $"('{database}'). Refusing to run against an unconfirmed target.");
        }

        var operatorIdRaw = Read(OperatorUserIdEnv);
        if (!int.TryParse(operatorIdRaw, out var operatorUserId) || operatorUserId <= 0)
        {
            return Disabled(
                $"{OperatorUserIdEnv} must be the operator's positive SIUser.Id on the target database.");
        }

        return new SqlTier(true, null, connection.Trim(), database, server, operatorUserId);

        static SqlTier Disabled(string reason) => new(false, reason, null, null, null, 0);
    }

    /// <summary>
    /// Resolves the Gmail tier. Independent of the SQL tier so the Pilot proofs can run alone.
    /// </summary>
    public static GmailTier TryResolveGmailTier()
    {
        if (!IsFlagSet(GmailEnabledEnv))
        {
            return new GmailTier(false, $"Set {GmailEnabledEnv}=1 to include the Gmail layer.", null, null);
        }

        var subject = Read(GmailSubjectEnv);
        if (string.IsNullOrWhiteSpace(subject))
        {
            return new GmailTier(
                false,
                $"{GmailSubjectEnv} is required: subject token, or '*' / 'AUTO' to pick the newest "
                + "AllMail message with attachments.",
                null,
                null);
        }

        var account = Read(GmailAccountEnv);
        if (string.IsNullOrWhiteSpace(account))
        {
            return new GmailTier(
                false,
                $"{GmailAccountEnv} is required: the mailbox address the stored token must authenticate as. "
                + "The token under %LOCALAPPDATA%\\SiNet\\google-token belongs to whichever account last "
                + "consented on this machine, which may not be the intended mailbox.",
                null,
                null);
        }

        return new GmailTier(true, null, subject.Trim(), account.Trim());
    }

    /// <summary>
    /// Resolves the ACC tier. Requires the Gmail tier, because the ACC steps operate on a real
    /// ingested Gmail message.
    /// </summary>
    public static AccTier TryResolveAccTier(GmailTier gmail)
    {
        ArgumentNullException.ThrowIfNull(gmail);

        if (!IsFlagSet(AccEnabledEnv))
        {
            return new AccTier(false, $"Set {AccEnabledEnv}=1 to include the ACC layer.", null, null);
        }

        if (!gmail.IsEnabled)
        {
            return new AccTier(
                false,
                $"The ACC layer requires the Gmail layer ({GmailEnabledEnv}=1): {gmail.SkipReason}",
                null,
                null);
        }

        var inboxProject = Read(AccInboxProjectEnv);
        if (string.IsNullOrWhiteSpace(inboxProject))
        {
            return new AccTier(
                false,
                $"{AccInboxProjectEnv} is required: the disposable ACC project name written temporarily "
                + "into the InboxProjectName system setting. Without it, ingest targets the office Inbox "
                + "project named by the restored DEV database (docs/ENVIRONMENTS.md §5.1.1).",
                null,
                null);
        }

        var place = Read(AccPlaceEnv);
        if (!string.Equals(place, RequiredAccPlaceTitle, StringComparison.Ordinal))
        {
            return new AccTier(
                false,
                $"{AccPlaceEnv} must be exactly '{RequiredAccPlaceTitle}' (docs/ENVIRONMENTS.md §5.1). "
                + $"Got '{place ?? "<null>"}'.",
                null,
                null);
        }

        return new AccTier(true, null, inboxProject.Trim(), place);
    }

    private static bool IsFlagSet(string name)
    {
        var value = Read(name);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
