using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

/// <summary>
/// SOF-003 graph: InternalApproval → SendQuote → SentFollowUp on QuoteSent.
/// </summary>
public sealed class ProposalSendQuoteStageTests
{
    [Fact]
    public async Task WhenInternalApprovalPassesThenSendQuoteThenSentFollowUp()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (projectId, emailId, defId) = await ProposalWorkflowHarness.SeedProjectAndEmailAsync(options);
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var completion = provider.GetRequiredService<ITaskCompletionService>();

            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    defId,
                    projectId,
                    WorkflowTriggerTypeDto.Email,
                    emailId,
                    ProposalWorkflowHarness.UserId,
                    "send-quote-graph",
                    IsProjectBound: false),
                CancellationToken.None);
            var instanceId = start.Instance.Id;

            await AdvanceToInternalApprovalAsync(commands, completion, options, instanceId);

            var approvalTask = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
                options, instanceId, TaskTypeCodes.ApproveQuoteInternal);

            var afterApproval = await completion.CompleteAsync(
                new CompleteTaskCommand(
                    approvalTask.Id,
                    ReviewCompletionEvents.QuoteInternallyApproved,
                    TaskResultCodes.QuoteApprovedInternally,
                    CompletedTaskLinkIds: null,
                    ProposalWorkflowHarness.UserId),
                CancellationToken.None);

            Assert.True(afterApproval.Success, afterApproval.ErrorMessage);
            Assert.True(afterApproval.WorkflowAdvanced);
            await AssertCurrentStageAsync(options, instanceId, ProposalStageCodes.SendQuote);

            var sendTask = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
                options, instanceId, TaskTypeCodes.SendQuoteToClient);

            var afterSend = await completion.CompleteAsync(
                new CompleteTaskCommand(
                    sendTask.Id,
                    ReviewCompletionEvents.QuoteSentToClient,
                    TaskResultCodes.QuoteSent,
                    CompletedTaskLinkIds: null,
                    ProposalWorkflowHarness.UserId),
                CancellationToken.None);

            Assert.True(afterSend.Success, afterSend.ErrorMessage);
            Assert.True(afterSend.WorkflowAdvanced);
            await AssertCurrentStageAsync(options, instanceId, ProposalStageCodes.SentFollowUp);

            var follow = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
                options, instanceId, TaskTypeCodes.FollowQuoteApproval);
            Assert.Equal(TaskTypeCodes.FollowQuoteApproval, follow.TaskType!.Code);

            var interaction = ReviewTaskInteractionRegistry.TryGet(TaskTypeCodes.FollowQuoteApproval);
            Assert.NotNull(interaction);
            Assert.Equal(TaskOpenMode.ProjectWork, interaction!.OpenMode);
            Assert.Contains(TaskResultCodes.QuoteCancelledNoResponse, interaction.AllowedTaskResultCodes);
        }
    }

    private static async Task AdvanceToInternalApprovalAsync(
        IWorkflowCommandService commands,
        ITaskCompletionService completion,
        DbContextOptions<SiNetSQLDbContext> options,
        int instanceId)
    {
        var intake = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, instanceId, TaskTypeCodes.IdentifyQuoteRequest);
        await ProposalWorkflowHarness.MarkTaskResultAsync(
            options, intake.Id, TaskResultCodes.QuoteRequestDetected);
        await commands.CheckAndAutoAdvanceAsync(
            new TaskClosedCommand(intake.Id, ProposalWorkflowHarness.UserId), CancellationToken.None);

        var open = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, instanceId, TaskTypeCodes.OpenQuoteProject);
        await ProposalWorkflowHarness.MarkTaskResultAsync(
            options, open.Id, TaskResultCodes.ProjectOpened);
        await commands.CheckAndAutoAdvanceAsync(
            new TaskClosedCommand(open.Id, ProposalWorkflowHarness.UserId), CancellationToken.None);

        var file = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, instanceId, TaskTypeCodes.FileQuoteMaterial);
        var fileResult = await completion.CompleteAsync(
            new CompleteTaskCommand(
                file.Id,
                ReviewCompletionEvents.ReviewMaterialFiled,
                TaskResultCode: null,
                CompletedTaskLinkIds: null,
                ProposalWorkflowHarness.UserId),
            CancellationToken.None);
        Assert.True(fileResult.Success, fileResult.ErrorMessage);

        var mat = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, instanceId, TaskTypeCodes.CheckQuoteMaterialCompleteness);
        var matResult = await completion.CompleteAsync(
            new CompleteTaskCommand(
                mat.Id,
                ReviewCompletionEvents.ReviewMaterialCheckCompleted,
                TaskResultCodes.MaterialComplete,
                null,
                ProposalWorkflowHarness.UserId),
            CancellationToken.None);
        Assert.True(matResult.Success, matResult.ErrorMessage);

        var calc = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, instanceId, TaskTypeCodes.PrepareQuoteCalculation);
        var calcResult = await completion.CompleteAsync(
            new CompleteTaskCommand(
                calc.Id,
                ReviewCompletionEvents.QuoteCalculationCompleted,
                TaskResultCodes.QuoteCalculationCompleted,
                null,
                ProposalWorkflowHarness.UserId),
            CancellationToken.None);
        Assert.True(calcResult.Success, calcResult.ErrorMessage);

        var prep = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, instanceId, TaskTypeCodes.PrepareQuoteDocument);
        var prepResult = await completion.CompleteAsync(
            new CompleteTaskCommand(
                prep.Id,
                ReviewCompletionEvents.QuoteDocumentPrepared,
                TaskResultCodes.QuotePrepared,
                null,
                ProposalWorkflowHarness.UserId),
            CancellationToken.None);
        Assert.True(prepResult.Success, prepResult.ErrorMessage);

        await AssertCurrentStageAsync(options, instanceId, ProposalStageCodes.InternalApproval);
    }

    private static async Task AssertCurrentStageAsync(
        DbContextOptions<SiNetSQLDbContext> options, int instanceId, string stageCode)
    {
        await using var db = new SiNetSQLDbContext(options);
        var instance = await db.WorkflowInstances
            .Include(i => i.CurrentStage)
            .FirstAsync(i => i.Id == instanceId);
        Assert.Equal(stageCode, instance.CurrentStage!.Code);
    }
}
