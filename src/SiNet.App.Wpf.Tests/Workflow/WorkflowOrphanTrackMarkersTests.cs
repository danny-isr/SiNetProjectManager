using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class WorkflowOrphanTrackMarkersTests
{
    [Fact]
    public void PrependMarker_adds_prefix_once()
    {
        var first = WorkflowOrphanTrackMarkers.PrependMarker("prior note", jobTypeId: 7, utcNow: new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc));
        Assert.StartsWith(WorkflowOrphanTrackMarkers.NotesPrefix, first, StringComparison.Ordinal);
        Assert.Contains("prior note", first, StringComparison.Ordinal);

        var second = WorkflowOrphanTrackMarkers.PrependMarker(first, jobTypeId: 7, utcNow: DateTime.UtcNow);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IsMarked_detects_prefix()
    {
        Assert.False(WorkflowOrphanTrackMarkers.IsMarked(null));
        Assert.False(WorkflowOrphanTrackMarkers.IsMarked("plain"));
        Assert.True(WorkflowOrphanTrackMarkers.IsMarked("[ORPHAN-TRACK] JobTypeId=1"));
    }
}
