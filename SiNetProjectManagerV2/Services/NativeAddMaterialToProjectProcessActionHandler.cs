using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SiNetSQL.Domain.Actions;
using SiNetSQL.Models;
using NativeFiling = SiNet.Infrastructure.Sql.Services.Files;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Native (Phase 3c) Process Action handler for <see cref="ActionCodes.AddMaterialToProject"/>.
///
/// Faithful port of the legacy <c>SiNetSQL.Domain.Actions.Handlers.AddMaterialToProjectProcessActionHandler</c>,
/// with one difference: filing is driven through the native
/// <see cref="NativeFiling.IProjectFileFilingService"/> (SiNet.Infrastructure.Sql, ported in Phase 3a)
/// instead of the legacy <c>SiNetSQL.Services.Files.IProjectFileFilingService</c>.
/// <para>
/// It still implements the legacy <see cref="IProcessActionHandler"/> contract so the existing
/// New-System trigger points (FileImportContinuationApplicationService / ActionExecutor →
/// IProcessActionDispatcher) keep working unchanged — only the filing engine underneath goes native.
/// This handler lives in the host because it bridges the legacy action contract (SiNetSQL) with the
/// native filing service (Infrastructure.Sql); the clean module cannot reference SiNetSQL (cycle).
/// </para>
/// </summary>
public sealed class NativeAddMaterialToProjectProcessActionHandler : IProcessActionHandler
{
    private readonly NativeFiling.IProjectFileFilingService _filingService;

    public NativeAddMaterialToProjectProcessActionHandler(NativeFiling.IProjectFileFilingService filingService)
    {
        _filingService = filingService ?? throw new ArgumentNullException(nameof(filingService));
    }

    public string ActionCode => ActionCodes.AddMaterialToProject;

    public async ValueTask<ProcessActionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectId = context.ProjectId ?? TryGetInt(context.Data, "ProjectId") ?? 0;
        var projectFileId = context.ProjectFileId ?? TryGetInt(context.Data, "ProjectFileId") ?? 0;
        var projectAlternativeId = TryGetInt(context.Data, "ProjectAlternativeId");
        var sourceLocalPath = TryGetString(context.Data, "SourceLocalPath");
        var originalFileName = TryGetString(context.Data, "OriginalFileName");

        var missing = new List<string>();
        if (projectId <= 0) missing.Add("ProjectId");
        if (projectFileId <= 0) missing.Add("ProjectFileId");
        if (string.IsNullOrWhiteSpace(sourceLocalPath)) missing.Add("SourceLocalPath");
        if (string.IsNullOrWhiteSpace(originalFileName)) missing.Add("OriginalFileName");

        if (missing.Count > 0)
        {
            return ProcessActionResult.Deferred(
                ActionCode,
                message: "נדרשת בחירת קובץ ויעד לפרויקט (" + string.Join(", ", missing) + ").",
                outcome: ActionOutcomes.RequiresUi);
        }

        if (!File.Exists(sourceLocalPath))
        {
            return ProcessActionResult.Failed(
                ActionCode,
                $"קובץ המקור לא נמצא: {sourceLocalPath}");
        }

        var sourceType = TryGetSourceType(context.Data) ?? FileInstanceSourceType.Manual;
        var sourceEmailAttachmentId = TryGetInt(context.Data, "SourceEmailAttachmentId");
        var emailSubject = TryGetString(context.Data, "EmailSubject");
        var emailFrom = TryGetString(context.Data, "EmailFrom");
        var emailDate = TryGetString(context.Data, "EmailDate");

        var request = new NativeFiling.FileProjectFileRequest(
            ProjectId: projectId,
            ProjectFileId: projectFileId,
            ProjectAlternativeId: projectAlternativeId,
            SourceLocalPath: sourceLocalPath!,
            OriginalFileName: originalFileName!,
            SourceType: sourceType,
            SourceEmailAttachmentId: sourceEmailAttachmentId,
            EmailSubject: emailSubject,
            EmailFrom: emailFrom,
            EmailDate: emailDate);

        NativeFiling.FileProjectFileResult result;
        try
        {
            result = await _filingService.FileAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SiNetSQL.Services.AppLogger.LogError(ex, $"[AddMaterialToProject][Native] FileAsync failed for ProjectId={projectId}, ProjectFileId={projectFileId}");
            return ProcessActionResult.Failed(ActionCode, $"קליטת קובץ לפרויקט נכשלה: {ex.Message}", ex);
        }

        var data = new Dictionary<string, object?>
        {
            ["ProjectId"] = projectId,
            ["ProjectFileId"] = projectFileId,
            ["ProjectAlternativeId"] = projectAlternativeId,
            ["PlacedFileName"] = result.PlacedFileName,
            ["PlacedFilePath"] = result.PlacedFilePath,
            ["StorageDestination"] = result.StorageDestination,
            ["CurrentVersionNumber"] = result.CurrentVersionNumber,
        };

        return new ProcessActionResult
        {
            ActionCode = ActionCode,
            Status = ActionExecutionStatus.Completed,
            Outcome = ActionOutcomes.Succeeded,
            Message = $"הקובץ '{result.PlacedFileName}' נקלט לפרויקט בהצלחה.",
            Data = data,
        };
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var raw) || raw is null) return null;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var raw) || raw is null) return null;
        return raw as string;
    }

    private static FileInstanceSourceType? TryGetSourceType(IReadOnlyDictionary<string, object?> data)
    {
        if (data is null || !data.TryGetValue("SourceType", out var raw) || raw is null) return null;
        return raw switch
        {
            FileInstanceSourceType t => t,
            int i when Enum.IsDefined(typeof(FileInstanceSourceType), i) => (FileInstanceSourceType)i,
            string s when Enum.TryParse<FileInstanceSourceType>(s, ignoreCase: true, out var parsed) => parsed,
            _ => null,
        };
    }
}
