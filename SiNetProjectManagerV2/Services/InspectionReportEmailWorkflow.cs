using System.Windows;
using SiNetSQL.DTOs.Email;
using SiNetSQL.Services.EmailOutbound;

namespace SiNetProjectManagerV2.Services;

public sealed class InspectionReportEmailWorkflow : IInspectionReportEmailWorkflow
{
    private readonly IInspectionReportEmailBuilder _builder;
    private readonly IEmailComposerService _composerService;

    public InspectionReportEmailWorkflow(
        IInspectionReportEmailBuilder builder,
        IEmailComposerService composerService)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _composerService = composerService ?? throw new ArgumentNullException(nameof(composerService));
    }

    public async Task<EmailSendResult?> OpenComposerAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _builder.BuildAsync(reportId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(context.UserMessage))
            {
                MessageBox.Show(context.UserMessage, "שליחת דוח במייל", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return await _composerService.ComposeAndSendAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "שליחת דוח במייל", MessageBoxButton.OK, MessageBoxImage.Error);
            return EmailSendResult.Failed(ex.Message);
        }
    }
}
