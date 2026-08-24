using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Reads the in-database proof that the target is the approved certification environment.
/// <para>
/// This type is deliberately <b>read-only</b>. A marker the harness could create would prove nothing at
/// all — the whole point is that a human placed it on the one database where destructive certification
/// runs are permitted. There is intentionally no write path here, and the key is not registered in
/// <c>SystemSettingKeys.AllManaged</c> so it never becomes an editable field in the settings UI.
/// </para>
/// <para>
/// Operator setup, run once per approved DEV database:
/// <code>
/// INSERT INTO SystemSettings (SettingKey, SettingValue, Description)
/// VALUES ('Certification.Environment', 'DEV', 'Approved target for SystemCertification writes.');
/// </code>
/// </para>
/// </summary>
internal static class SystemCertificationDatabaseMarker
{
    /// <summary>Marker key. Absent → the database is not an approved certification target.</summary>
    public const string SettingKey = "Certification.Environment";

    /// <summary>The only accepted marker value.</summary>
    public const string RequiredValue = "DEV";

    internal sealed record Result(bool IsApproved, string? Violation, string? FoundValue);

    /// <summary>
    /// Verifies the marker. Any outcome other than an exact <see cref="RequiredValue"/> match is a
    /// violation that must abort the run before the first write.
    /// </summary>
    public static async Task<Result> VerifyAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var value = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.SettingKey == SettingKey)
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync(cancellationToken);

        if (value is null)
        {
            return new Result(
                false,
                $"The target database has no '{SettingKey}' row, so it is not a declared certification "
                + "environment. The harness will not create it: a marker it could write would be no proof. "
                + $"Add it manually with value '{RequiredValue}' on the approved DEV database only.",
                null);
        }

        if (!string.Equals(value.Trim(), RequiredValue, StringComparison.OrdinalIgnoreCase))
        {
            return new Result(
                false,
                $"'{SettingKey}' is '{value}', not '{RequiredValue}'. Refusing to run destructive "
                + "certification writes against a database that does not declare itself as DEV.",
                value);
        }

        return new Result(true, null, value.Trim());
    }
}
