using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// Runtime DDL for <c>ProjectFile</c> catalog columns.
/// <para>
/// Status: <b>paused / inactive</b> (2026-07-26).
/// Why: competing with the normal EF migration workflow — it added <c>IsRequired</c>/<c>Code</c>
/// outside <c>__EFMigrationsHistory</c>, so <c>dotnet ef database update</c> failed with
/// "Column name 'IsRequired' … specified more than once".
/// Schema changes for these columns must come only from user-run EF migrations / efbundle.
/// May return later only if explicitly re-approved as a temporary bridge when Migrate is disabled.
/// </para>
/// </summary>
internal static class ProjectFileSchemaPatches
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _schemaEnsured; // 0 = not yet, 1 = done this process

    /// <summary>
    /// No-op while paused. Kept so call sites compile; does not alter the database.
    /// </summary>
    public static Task EnsureCatalogColumnsAsync(SiNetSQLDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        // Paused: do not run DDL. User-owned EF migrations own ProjectFile.IsRequired / Code.
        Interlocked.Exchange(ref _schemaEnsured, 1);
        return Task.CompletedTask;
    }

    /// <summary>Backward-compatible alias.</summary>
    public static Task EnsureIsRequiredColumnAsync(SiNetSQLDbContext db, CancellationToken ct = default)
        => EnsureCatalogColumnsAsync(db, ct);

    /// <summary>Test-only: reset the process-wide "ensured" flag.</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _schemaEnsured, 0);

    // --- Original DDL kept below for reference / possible future reactivation (do not delete) ---
    // Separate batches were required: CREATE INDEX on [Code] cannot share a batch with ALTER ADD [Code].
#pragma warning disable IDE0051 // Kept for reactivation; currently unused while paused.
    private static async Task EnsureCatalogColumns_ActiveAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!db.Database.IsRelational())
                return;

            await db.Database.ExecuteSqlRawAsync(
                    """
                    IF COL_LENGTH(N'dbo.ProjectFile', N'IsRequired') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[ProjectFile]
                        ADD [IsRequired] bit NOT NULL
                            CONSTRAINT [DF_ProjectFile_IsRequired] DEFAULT (CONVERT([bit],(0)));
                    END
                    """,
                    ct)
                .ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync(
                    """
                    IF COL_LENGTH(N'dbo.ProjectFile', N'Code') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[ProjectFile]
                        ADD [Code] nvarchar(64) NULL;
                    END
                    """,
                    ct)
                .ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync(
                    """
                    IF COL_LENGTH(N'dbo.ProjectFile', N'Code') IS NOT NULL
                       AND NOT EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE [name] = N'ux_ProjectFile_Code'
                          AND [object_id] = OBJECT_ID(N'dbo.ProjectFile'))
                    BEGIN
                        CREATE UNIQUE NONCLUSTERED INDEX [ux_ProjectFile_Code]
                        ON [dbo].[ProjectFile] ([Code])
                        WHERE [Code] IS NOT NULL;
                    END
                    """,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
#pragma warning restore IDE0051
}
