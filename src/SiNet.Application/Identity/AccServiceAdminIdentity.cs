using SiNet.Application.Settings;

namespace SiNet.Application.Identity;

/// <summary>
/// Compares the Autodesk profile behind the AccService 3-legged Admin token
/// to <see cref="SystemSettingKeys.AccBootstrapAdminEmail"/>. Never compares to SIUser.
/// </summary>
public static class AccServiceAdminIdentity
{
    /// <summary>Canonical code default when the DB row is missing.</summary>
    public const string DefaultExpectedAdminEmail = SystemSettingsDefaults.AccBootstrapAdminEmail;

    /// <summary>
    /// Resolves expected vs actual Admin identity (emails only). Optional Admin API status
    /// is layered via <see cref="WithAdminApiStatus"/> after identity MATCH.
    /// </summary>
    public static AccServiceAdminIdentityCheck Evaluate(
        string? expectedAdminEmail,
        string? actualAdminEmail,
        bool tokenAvailable = true,
        bool profileResolved = true,
        string? autodeskUserId = null,
        string? displayName = null,
        string? adminApiStatus = null)
    {
        var expected = IdentityEmailComparer.Normalize(expectedAdminEmail)
            ?? IdentityEmailComparer.Normalize(DefaultExpectedAdminEmail)
            ?? DefaultExpectedAdminEmail;

        if (!tokenAvailable)
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ActualAdminEmail: null,
                TokenAvailable: false,
                ProfileResolved: false,
                AutodeskUserId: null,
                DisplayName: null,
                EmailMatch: false,
                Status: AccServiceAdminIdentityStatus.TokenMissing,
                AdminApiStatus: adminApiStatus,
                FailureReason: "AccService 3-legged Admin token is missing.",
                OperatorMessageHe: FormatMismatchMessageHe(expected, "(חסר טוקן)"));
        }

        if (!profileResolved || string.IsNullOrWhiteSpace(actualAdminEmail))
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ActualAdminEmail: IdentityEmailComparer.Normalize(actualAdminEmail),
                TokenAvailable: true,
                ProfileResolved: false,
                AutodeskUserId: autodeskUserId,
                DisplayName: displayName,
                EmailMatch: false,
                Status: AccServiceAdminIdentityStatus.ProfileUnavailable,
                AdminApiStatus: adminApiStatus,
                FailureReason: "AccService Autodesk Admin profile could not be resolved.",
                OperatorMessageHe: FormatMismatchMessageHe(expected, "(פרופיל לא זמין)"));
        }

        var actual = IdentityEmailComparer.Normalize(actualAdminEmail)!;
        var emailMatch = IdentityEmailComparer.EqualsNormalized(expected, actual);

        if (!emailMatch)
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ActualAdminEmail: actual,
                TokenAvailable: true,
                ProfileResolved: true,
                AutodeskUserId: autodeskUserId,
                DisplayName: displayName,
                EmailMatch: false,
                Status: AccServiceAdminIdentityStatus.AdminEmailMismatch,
                AdminApiStatus: adminApiStatus,
                FailureReason:
                    $"AccService Autodesk admin account mismatch. Expected: {expected}; Connected: {actual}",
                OperatorMessageHe: FormatMismatchMessageHe(expected, actual));
        }

        if (IsAdminApiUnauthorized(adminApiStatus))
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ActualAdminEmail: actual,
                TokenAvailable: true,
                ProfileResolved: true,
                AutodeskUserId: autodeskUserId,
                DisplayName: displayName,
                EmailMatch: true,
                Status: AccServiceAdminIdentityStatus.AdminApiUnauthorized,
                AdminApiStatus: adminApiStatus,
                FailureReason: "AccService Admin identity matches but Admin APIs returned unauthorized (403).",
                OperatorMessageHe:
                    "ACC Admin: החשבון נכון אך חסרות הרשאות Account Admin");
        }

        if (IsAdminApiUnavailable(adminApiStatus))
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ActualAdminEmail: actual,
                TokenAvailable: true,
                ProfileResolved: true,
                AutodeskUserId: autodeskUserId,
                DisplayName: displayName,
                EmailMatch: true,
                Status: AccServiceAdminIdentityStatus.ServiceUnavailable,
                AdminApiStatus: adminApiStatus,
                FailureReason: "AccService Admin identity matches but Admin API probe was unavailable.",
                OperatorMessageHe: null);
        }

        return new AccServiceAdminIdentityCheck(
            ExpectedAdminEmail: expected,
            ActualAdminEmail: actual,
            TokenAvailable: true,
            ProfileResolved: true,
            AutodeskUserId: autodeskUserId,
            DisplayName: displayName,
            EmailMatch: true,
            Status: AccServiceAdminIdentityStatus.Healthy,
            AdminApiStatus: adminApiStatus,
            FailureReason: null,
            OperatorMessageHe: null);
    }

    /// <summary>Re-evaluate after an Admin API probe (only meaningful when identity already matches).</summary>
    public static AccServiceAdminIdentityCheck WithAdminApiStatus(
        AccServiceAdminIdentityCheck identity,
        string? adminApiStatus)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Status is AccServiceAdminIdentityStatus.AdminEmailMismatch
            or AccServiceAdminIdentityStatus.TokenMissing
            or AccServiceAdminIdentityStatus.ProfileUnavailable)
        {
            return identity with { AdminApiStatus = adminApiStatus };
        }

        return Evaluate(
            identity.ExpectedAdminEmail,
            identity.ActualAdminEmail,
            tokenAvailable: identity.TokenAvailable,
            profileResolved: identity.ProfileResolved,
            autodeskUserId: identity.AutodeskUserId,
            displayName: identity.DisplayName,
            adminApiStatus: adminApiStatus);
    }

    /// <summary>Fail-closed when expected is configured and connected is a known different email.</summary>
    public static bool IsKnownWrongAdmin(AccServiceAdminIdentityCheck check) =>
        check.Status == AccServiceAdminIdentityStatus.AdminEmailMismatch;

    /// <summary>
    /// Block AccService Admin mutations until token profile is resolved and emails match.
    /// AdminApiUnauthorized is reported separately after identity MATCH; it is not this gate.
    /// </summary>
    public static bool ShouldBlockAdminMutation(AccServiceAdminIdentityCheck check) =>
        !check.EmailMatch
        || !check.ProfileResolved
        || check.Status is AccServiceAdminIdentityStatus.AdminEmailMismatch
            or AccServiceAdminIdentityStatus.TokenMissing
            or AccServiceAdminIdentityStatus.ProfileUnavailable;

    public static string FormatMismatchMessageHe(string expected, string actual) =>
        "חשבון ה-Autodesk של AccService אינו תואם להגדרת המערכת." + Environment.NewLine +
        Environment.NewLine +
        "החשבון המוגדר:" + Environment.NewLine +
        expected + Environment.NewLine +
        Environment.NewLine +
        "החשבון המחובר:" + Environment.NewLine +
        actual + Environment.NewLine +
        Environment.NewLine +
        "יש להתחבר מחדש ל-AccService באמצעות החשבון המוגדר.";

    private static bool IsAdminApiUnauthorized(string? adminApiStatus) =>
        !string.IsNullOrWhiteSpace(adminApiStatus)
        && (adminApiStatus.Contains("403", StringComparison.Ordinal)
            || string.Equals(adminApiStatus.Trim(), "unauthorized", StringComparison.OrdinalIgnoreCase));

    private static bool IsAdminApiUnavailable(string? adminApiStatus) =>
        !string.IsNullOrWhiteSpace(adminApiStatus)
        && (string.Equals(adminApiStatus.Trim(), "unavailable", StringComparison.OrdinalIgnoreCase)
            || adminApiStatus.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
}

public enum AccServiceAdminIdentityStatus
{
    Healthy = 0,
    AdminEmailMismatch = 1,
    TokenMissing = 2,
    ProfileUnavailable = 3,
    AdminApiUnauthorized = 4,
    ServiceUnavailable = 5,
}

public sealed record AccServiceAdminIdentityCheck(
    string ExpectedAdminEmail,
    string? ActualAdminEmail,
    bool TokenAvailable,
    bool ProfileResolved,
    string? AutodeskUserId,
    string? DisplayName,
    bool EmailMatch,
    AccServiceAdminIdentityStatus Status,
    string? AdminApiStatus,
    string? FailureReason,
    string? OperatorMessageHe)
{
    /// <summary>Backward-compatible alias for <see cref="ActualAdminEmail"/>.</summary>
    public string? ConnectedProfileEmail => ActualAdminEmail;

    /// <summary>Backward-compatible alias for <see cref="OperatorMessageHe"/> / <see cref="FailureReason"/>.</summary>
    public string? WarningMessage => OperatorMessageHe ?? FailureReason;
}
