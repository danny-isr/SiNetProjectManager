using SiNet.App.Wpf.Surfaces.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailEffectiveFilingTests
{
    [Fact]
    public void ShowMoveBlockReason_hidden_until_gmail_assigned_layout()
    {
        var bar = new EmailActionBarViewModel(() => Task.CompletedTask, () => Task.CompletedTask)
        {
            MoveBlockReason = "המייל לא משויך לפרויקט.",
            ShowUnassignedLayout = true,
            ShowAssignedLayout = false,
        };

        Assert.False(bar.ShowMoveBlockReason);

        bar.ShowAssignedLayout = true;
        Assert.True(bar.ShowMoveBlockReason);
    }
}
