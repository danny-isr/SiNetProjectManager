namespace SiNet.Application.Identity;

/// <summary>
/// Compares the Autodesk profile behind the AccService 3-legged Admin token
/// to <c>AccService.ExpectedAdminEmail</c>. Never compares to SIUser.
/// </summary>
public static class AccServiceAdminIdentity
{
    public const string DefaultExpectedAdminEmail = "siad@si-eng.co.il";

    public static AccServiceAdminIdentityCheck Evaluate(string? expectedAdminEmail, string? connectedProfileEmail)
    {
        var expected = IdentityEmailComparer.Normalize(expectedAdminEmail)
            ?? IdentityEmailComparer.Normalize(DefaultExpectedAdminEmail);
        var connected = IdentityEmailComparer.Normalize(connectedProfileEmail);

        if (expected is null)
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: null,
                ConnectedProfileEmail: connected,
                Status: AccServiceAdminIdentityStatus.ExpectedNotConfigured,
                WarningMessage: null);
        }

        if (connected is null)
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ConnectedProfileEmail: null,
                Status: AccServiceAdminIdentityStatus.ConnectedUnknown,
                WarningMessage:
                    $"AccService Autodesk admin account mismatch.{Environment.NewLine}" +
                    $"Expected: {expected}{Environment.NewLine}" +
                    "Connected: (unavailable)");
        }

        if (IdentityEmailComparer.EqualsNormalized(expected, connected))
        {
            return new AccServiceAdminIdentityCheck(
                ExpectedAdminEmail: expected,
                ConnectedProfileEmail: connected,
                Status: AccServiceAdminIdentityStatus.Match,
                WarningMessage: null);
        }

        return new AccServiceAdminIdentityCheck(
            ExpectedAdminEmail: expected,
            ConnectedProfileEmail: connected,
            Status: AccServiceAdminIdentityStatus.Mismatch,
            WarningMessage:
                $"AccService Autodesk admin account mismatch.{Environment.NewLine}" +
                $"Expected: {expected}{Environment.NewLine}" +
                $"Connected: {connected}");
    }

    /// <summary>Fail-closed when expected is configured and connected is a known different email.</summary>
    public static bool IsKnownWrongAdmin(AccServiceAdminIdentityCheck check) =>
        check.Status == AccServiceAdminIdentityStatus.Mismatch;
}

public enum AccServiceAdminIdentityStatus
{
    Match = 0,
    Mismatch = 1,
    ExpectedNotConfigured = 2,
    ConnectedUnknown = 3,
}

public sealed record AccServiceAdminIdentityCheck(
    string? ExpectedAdminEmail,
    string? ConnectedProfileEmail,
    AccServiceAdminIdentityStatus Status,
    string? WarningMessage);
