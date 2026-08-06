using SiNet.App.Wpf.Surfaces.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailActionBarReadStateTests
{
    [Fact]
    public void Mark_as_read_toggle_defaults_off_in_all_builds()
    {
        var bar = new EmailActionBarViewModel(() => Task.CompletedTask, () => Task.CompletedTask);

        Assert.False(EmailActionBarViewModel.DefaultMarkAsReadEnabled);
        Assert.False(bar.MarkAsReadEnabled);
    }

    [Fact]
    public void Operator_can_flip_the_mark_as_read_toggle()
    {
        var bar = new EmailActionBarViewModel(() => Task.CompletedTask, () => Task.CompletedTask)
        {
            MarkAsReadEnabled = true,
        };

        Assert.True(bar.MarkAsReadEnabled);
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

    [Fact]
    public void Fyi_command_is_disabled_until_can_mark_as_fyi()
    {
        var called = false;
        var bar = new EmailActionBarViewModel(
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            openInGmail: null,
            markAsFyiAsync: () =>
            {
                called = true;
                return Task.CompletedTask;
            });

        Assert.False(bar.MarkAsFyiCommand.CanExecute(null));
        bar.CanMarkAsFyi = true;
        Assert.True(bar.MarkAsFyiCommand.CanExecute(null));
        bar.MarkAsFyiCommand.Execute(null);
        Assert.True(called);
    }
}
