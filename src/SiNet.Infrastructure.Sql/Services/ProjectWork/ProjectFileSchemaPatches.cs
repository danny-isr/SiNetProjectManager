using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// Idempotent schema patches for <c>ProjectFile</c> that must run even when automatic
/// <c>Database.Migrate()</c> is disabled (production uses efbundle).
/// </summary>
internal static class ProjectFileSchemaPatches
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _schemaEnsured; // 0 = not yet, 1 = done this process

    /// <summary>
    /// Ensures <c>ProjectFile.IsRequired</c> and <c>ProjectFile.Code</c> exist. Safe to call repeatedly.
    /// </summary>
    public static async Task EnsureCatalogColumnsAsync(SiNetSQLDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (Interlocked.CompareExchange(ref _schemaEnsured, 0, 0) == 1)
            return;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _schemaEnsured) == 1)
                return;

            // InMemory / non-relational providers used in tests have no SQL DDL surface.
            if (!db.Database.IsRelational())
            {
                Interlocked.Exchange(ref _schemaEnsured, 1);
                return;
            }

            await db.Database.ExecuteSqlRawAsync(
                    """
                    IF COL_LENGTH(N'dbo.ProjectFile', N'IsRequired') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[ProjectFile]
                        ADD [IsRequired] bit NOT NULL
                            CONSTRAINT [DF_ProjectFile_IsRequired] DEFAULT (CONVERT([bit],(0)));
                    END

                    IF COL_LENGTH(N'dbo.ProjectFile', N'Code') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[ProjectFile]
                        ADD [Code] nvarchar(64) NULL;
                    END

                    IF NOT EXISTS (
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

            await db.Database.ExecuteSqlRawAsync(
                    """
                    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                            WHERE [MigrationId] = N'20260726140000_AddProjectFileIsRequired')
                        BEGIN
                            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                            VALUES (N'20260726140000_AddProjectFileIsRequired', N'10.0.7');
                        END

                        IF NOT EXISTS (
                            SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                            WHERE [MigrationId] = N'20260726190000_AddProjectFileCode')
                        BEGIN
                            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                            VALUES (N'20260726190000_AddProjectFileCode', N'10.0.7');
                        END
                    END
                    """,
                    ct)
                .ConfigureAwait(false);

            Interlocked.Exchange(ref _schemaEnsured, 1);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Backward-compatible alias.</summary>
    public static Task EnsureIsRequiredColumnAsync(SiNetSQLDbContext db, CancellationToken ct = default)
        => EnsureCatalogColumnsAsync(db, ct);

    /// <summary>Test-only: reset the process-wide "ensured" flag.</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _schemaEnsured, 0);
}
