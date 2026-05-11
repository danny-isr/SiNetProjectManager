using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2.Windows;
using SiNetSQL.DTOs.Email;
using SiNetSQL.Services.EmailOutbound;
using SiOffice.GoogleConnector;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManagerV2.Services;

public sealed class EmailComposerService : IEmailComposerService
{
    private readonly IOutboundMailService _mailService;
    private readonly ProjectRecipientCacheService _recipientCacheService;

    public EmailComposerService(
        IOutboundMailService mailService,
        ProjectRecipientCacheService recipientCacheService)
    {
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));
        _recipientCacheService = recipientCacheService ?? throw new ArgumentNullException(nameof(recipientCacheService));
    }

    public Task<EmailSendResult?> ComposeAndSendAsync(
        EmailComposerContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReportLogger.Info(
            $"Operation=EmailComposer Action=Opened TaskId={context.TaskId?.ToString() ?? "(none)"} WorkflowId={context.WorkflowId?.ToString() ?? "(none)"}");

        var viewModel = new EmailComposerViewModel(context, _mailService);
        var window = new EmailComposerWindow
        {
            DataContext = viewModel,
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        _ = LoadRecipientCacheInBackgroundAsync(context, viewModel, cancellationToken);

        viewModel.RequestClose += result =>
        {
            window.DialogResult = result;
            window.Close();
        };

        var dialogResult = window.ShowDialog();
        var sendResult = viewModel.SendResult;

        var action = dialogResult == true && sendResult?.Success == true ? "Sent" : "Cancelled";
        ReportLogger.Info(
            $"Operation=EmailComposer Action={action} TaskId={context.TaskId?.ToString() ?? "(none)"} WorkflowId={context.WorkflowId?.ToString() ?? "(none)"}");

        return Task.FromResult(sendResult);
    }

    private async Task LoadRecipientCacheInBackgroundAsync(
        EmailComposerContext context,
        EmailComposerViewModel viewModel,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.EntityType, "InspectionReport", StringComparison.OrdinalIgnoreCase)
            || context.EntityId == null)
        {
            return;
        }

        try
        {
            var projectId = await ResolveProjectIdAsync(context.EntityId.Value, cancellationToken);
            if (projectId == null)
                return;

            var suggestions = await _recipientCacheService.LoadAsync(projectId.Value, cancellationToken);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                viewModel.AddRecipientSuggestions(suggestions);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ReportLogger.Warn($"Operation=EmailRecipientCacheLoad ProjectId=(unknown) Source=GmailLabel Result=Failed Reason={ex.Message}");
        }
    }

    private static async Task<int?> ResolveProjectIdAsync(int reportId, CancellationToken cancellationToken)
    {
        await using var db = await App.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>()
            .CreateDbContextAsync(cancellationToken);

        return await db.InspectionReports
            .AsNoTracking()
            .Where(r => r.ReportId == reportId)
            .Select(r => (int?)r.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
