using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// One-time JobType upgrade on application startup. It uses
/// <see cref="CanonicalJobTypeReconciliation"/> and does not run the DEBUG general seed.
/// A second start, or a database that is already canonical, does not write.
/// </summary>
public static class CanonicalJobTypeStartupUpgrade
{
    public const string LockResource = "SiNet:CanonicalJobTypeUpgrade";

    public static async Task<CanonicalJobTypeStartupUpgradeResult> RunAsync(
        SiNetSQLDbContext db,
        CancellationToken ct,
        int lockTimeoutMs = 60_000)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!db.Database.IsRelational())
            return await RunCoreAsync(db, ct).ConfigureAwait(false);

        await using var gate = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var lockCode = await TryAcquireLockAsync(db, lockTimeoutMs, ct).ConfigureAwait(false);
        if (lockCode < 0)
        {
            await gate.RollbackAsync(ct).ConfigureAwait(false);
            return CanonicalJobTypeStartupUpgradeResult.Busy(lockCode);
        }

        try
        {
            var result = await RunCoreAsync(db, ct).ConfigureAwait(false);
            if (result.Outcome is CanonicalJobTypeStartupUpgradeOutcome.Blocked
                or CanonicalJobTypeStartupUpgradeOutcome.Failed)
            {
                await gate.RollbackAsync(ct).ConfigureAwait(false);
                return result;
            }

            await gate.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await gate.RollbackAsync(ct).ConfigureAwait(false);
            return CanonicalJobTypeStartupUpgradeResult.Failed(ex);
        }
    }

    private static async Task<CanonicalJobTypeStartupUpgradeResult> RunCoreAsync(
        SiNetSQLDbContext db, CancellationToken ct)
    {
        var preview = await CanonicalJobTypeReconciliation.PreviewAsync(db, ct).ConfigureAwait(false);
        if (preview.Conflicts.Count > 0)
            return CanonicalJobTypeStartupUpgradeResult.Blocked(preview.Conflicts);

        if (!await NeedsWriteAsync(db, preview, ct).ConfigureAwait(false))
            return CanonicalJobTypeStartupUpgradeResult.AlreadyCurrent(preview.ReviewJobTypeId);

        try
        {
            await CanonicalJobTypeReconciliation.ApplyAsync(db, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            return CanonicalJobTypeStartupUpgradeResult.Failed(ex);
        }

        var after = await CanonicalJobTypeReconciliation.PreviewAsync(db, ct).ConfigureAwait(false);
        return CanonicalJobTypeStartupUpgradeResult.Upgraded(after.ReviewJobTypeId);
    }

    private static async Task<bool> NeedsWriteAsync(
        SiNetSQLDbContext db,
        CanonicalJobTypeReconciliationReport preview,
        CancellationToken ct)
    {
        if (preview.LegacyJobTypeIds.Count > 0)
            return true;

        if (preview.ReviewJobTypeId is int reviewId
            && !await IsWorkflowReadyAsync(
                db, reviewId, WorkflowCodes.Review, ReviewWorkflowSeedData.Stages.Select(s => s.Code).ToArray(), ct)
                .ConfigureAwait(false))
            return true;

        if (preview.OpinionJobTypeId is int opinionId
            && !await IsWorkflowReadyAsync(
                db, opinionId, WorkflowCodes.Opinion, OpinionWorkflowSeedData.Stages.Select(s => s.Code).ToArray(), ct)
                .ConfigureAwait(false))
            return true;

        return false;
    }

    private static async Task<bool> IsWorkflowReadyAsync(
        SiNetSQLDbContext db,
        int jobTypeId,
        string workflowCode,
        IReadOnlyList<string> stageCodes,
        CancellationToken ct)
    {
        var workflowId = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == workflowCode && d.IsActive)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (workflowId is null)
            return false;

        var mapping = await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
            .Where(m => m.ProjectTypeId == jobTypeId && m.WorkflowDefinitionId == workflowId)
            .Select(m => new { m.IsEnabled, m.IsDefault })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (mapping is not { IsEnabled: true, IsDefault: true })
            return false;

        var planningId = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.PlanningWorkflow)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (planningId is int planning && planning != workflowId)
        {
            var planningEnabled = await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
                .AnyAsync(m =>
                    m.ProjectTypeId == jobTypeId
                    && m.WorkflowDefinitionId == planning
                    && (m.IsEnabled || m.IsDefault), ct)
                .ConfigureAwait(false);
            if (planningEnabled)
                return false;
        }

        var activeCodes = await db.ProjectTypeWorkflowStages.AsNoTracking()
            .Where(s => s.ProjectTypeId == jobTypeId && s.IsActive)
            .Join(
                db.WorkflowStageDefinitions.AsNoTracking().Where(st => st.WorkflowDefinitionId == workflowId),
                s => s.WorkflowStageDefinitionId,
                st => st.Id,
                (_, st) => st.Code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var present = new HashSet<string>(activeCodes.Where(c => c != null)!, StringComparer.OrdinalIgnoreCase);
        return stageCodes.All(present.Contains);
    }

    private static async Task<int> TryAcquireLockAsync(
        SiNetSQLDbContext db, int lockTimeoutMs, CancellationToken ct)
    {
        var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resource = new SqlParameter("@Resource", LockResource);
        var timeout = new SqlParameter("@LockTimeout", lockTimeoutMs);
        await db.Database.ExecuteSqlRawAsync(
                "EXEC @Result = sp_getapplock @Resource=@Resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=@LockTimeout;",
                [result, resource, timeout],
                ct)
            .ConfigureAwait(false);
        return result.Value is int code ? code : -999;
    }
}

public enum CanonicalJobTypeStartupUpgradeOutcome
{
    AlreadyCurrent,
    Upgraded,
    Blocked,
    Failed,
    Busy,
}

public sealed record CanonicalJobTypeStartupUpgradeResult(
    CanonicalJobTypeStartupUpgradeOutcome Outcome,
    int? ReviewJobTypeId,
    string Message)
{
    public static CanonicalJobTypeStartupUpgradeResult AlreadyCurrent(int? reviewJobTypeId) =>
        new(CanonicalJobTypeStartupUpgradeOutcome.AlreadyCurrent, reviewJobTypeId,
            "סוגי העבודה כבר תקינים. לא בוצעה כתיבה.");

    public static CanonicalJobTypeStartupUpgradeResult Upgraded(int? reviewJobTypeId) =>
        new(CanonicalJobTypeStartupUpgradeOutcome.Upgraded, reviewJobTypeId,
            "סוג העבודה הישן נורמל ל«בדיקה». המזהה וקישורי הפרויקטים נשמרו.");

    public static CanonicalJobTypeStartupUpgradeResult Blocked(IReadOnlyList<string> conflicts) =>
        new(CanonicalJobTypeStartupUpgradeOutcome.Blocked, null,
            "שדרוג סוגי העבודה נעצר ולא שונה דבר במסד."
            + Environment.NewLine + string.Join(Environment.NewLine, conflicts)
            + Environment.NewLine
            + "אפשר להמשיך לעבוד במסכים שאינם תלויים בהטמעת תהליך. אשף «הטמעת תהליך קיים» יישאר בלי שלבים עד שההתנגשות תיפתר.");

    public static CanonicalJobTypeStartupUpgradeResult Failed(Exception ex) =>
        new(CanonicalJobTypeStartupUpgradeOutcome.Failed, null,
            "שדרוג סוגי העבודה נכשל והשינוי בוטל. המסד לא נשאר באמצע שדרוג."
            + Environment.NewLine + ex.Message
            + Environment.NewLine
            + "אפשר להמשיך לעבוד במסכים שאינם תלויים בהטמעת תהליך.");

    public static CanonicalJobTypeStartupUpgradeResult Busy(int lockCode) =>
        new(CanonicalJobTypeStartupUpgradeOutcome.Busy, null,
            "מחשב אחר משדרג כרגע את סוגי העבודה. לא הורץ שדרוג נוסף."
            + Environment.NewLine
            + "אפשר להמשיך לעבוד. אם אשף ההטמעה עדיין בלי שלבים, פתחו את האפליקציה שוב אחרי שהשדרוג השני יסתיים."
            + $" (lock {lockCode})");
}
