using SiNet.App.Wpf.Surfaces.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailActionBarReadStateTests
{
    [Fact]
    public void Mark_as_read_toggle_defaults_from_the_build()
    {
        var bar = new EmailActionBarViewModel(() => Task.CompletedTask, () => Task.CompletedTask);

        Assert.Equal(EmailActionBarViewModel.DefaultMarkAsReadEnabled, bar.MarkAsReadEnabled);
#if DEBUG
        Assert.False(bar.MarkAsReadEnabled);
#else
        Assert.True(bar.MarkAsReadEnabled);
#endif
    }

    [Fact]
    public void Operator_can_flip_the_mark_as_read_toggle_in_either_build()
    {
        var bar = new EmailActionBarViewModel(() => Task.CompletedTask, () => Task.CompletedTask)
        {
            MarkAsReadEnabled = !EmailActionBarViewModel.DefaultMarkAsReadEnabled,
        };

        Assert.Equal(!EmailActionBarViewModel.DefaultMarkAsReadEnabled, bar.MarkAsReadEnabled);
    }

    [Fact]
    public void Open_in_gmail_command_is_disabled_until_a_message_is_selected()
    {
        var opened = false;
        var bar = new EmailActionBarViewModel(
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => opened = true);

        Assert.False(bar.OpenInGmailCommand.CanExecute(null));
        bar.OpenInGmailCommand.Execute(null);
        Assert.False(opened);

        bar.CanOpenInGmail = true;
        Assert.True(bar.OpenInGmailCommand.CanExecute(null));
        bar.OpenInGmailCommand.Execute(null);
        Assert.True(opened);
    }
}
