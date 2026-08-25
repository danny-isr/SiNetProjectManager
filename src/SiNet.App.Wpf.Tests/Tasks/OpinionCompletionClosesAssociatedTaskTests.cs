using SiNet.Infrastructure.Sql.Services.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Tasks;

/// <summary>
/// Opinion ProjectWork tasks create a Pending Related WorkTarget. Without
/// <see cref="ReviewCompletionBehavior.ClosesAssociatedTask"/>, CompleteAsync records
/// Success=true but leaves the task open (work-targets-pending) — seen in OPN LIVE
/// certification on AnalyzeOpinionMaterials.
/// </summary>
public sealed class OpinionCompletionClosesAssociatedTaskTests
{
    [Theory]
    [InlineData(ReviewCompletionEvents.AnalysisCompleted)]
    [InlineData(ReviewCompletionEvents.DraftPrepared)]
    [InlineData(ReviewCompletionEvents.InternalReviewCompleted)]
    [InlineData(ReviewCompletionEvents.DocumentSent)]
    public void Opinion_project_work_completion_events_close_associated_task(string eventCode)
    {
        var behavior = ReviewCompletionEventBehavior.TryGet(eventCode);
        Assert.NotNull(behavior);
        Assert.True(
            behavior!.ClosesAssociatedTask,
            $"{eventCode} must set ClosesAssociatedTask so Pending ProjectWork targets do not soft-block close.");
    }
}
