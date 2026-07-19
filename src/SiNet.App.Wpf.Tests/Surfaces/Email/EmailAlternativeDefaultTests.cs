using SiNet.Application.Email.Detail;
using SiNet.App.Wpf.Surfaces.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailAlternativeDefaultTests
{
    [Fact]
    public void ResolveDefaultId_prefers_IsDefault_then_name_1_then_first()
    {
        Assert.Equal(
            20,
            EmailProjectAlternativeOption.ResolveDefaultId(
            [
                new EmailProjectAlternativeOption(10, "A", IsDefault: false),
                new EmailProjectAlternativeOption(20, "B", IsDefault: true),
            ]));

        Assert.Equal(
            11,
            EmailProjectAlternativeOption.ResolveDefaultId(
            [
                new EmailProjectAlternativeOption(10, "A", IsDefault: false),
                new EmailProjectAlternativeOption(11, "1", IsDefault: false),
            ]));

        Assert.Equal(
            10,
            EmailProjectAlternativeOption.ResolveDefaultId(
            [
                new EmailProjectAlternativeOption(10, "A", IsDefault: false),
                new EmailProjectAlternativeOption(11, "B", IsDefault: false),
            ]));
    }

    [Fact]
    public void ApplyTag_without_alternative_selects_default_one()
    {
        var item = new EmailDetailAttachmentItem(
            inboxAttachmentId: 1,
            fileName: "plan.dwf",
            kind: "Attachment",
            size: "1 KB",
            isTaggable: true,
            tagAsync: _ => Task.CompletedTask,
            alternativeChangedAsync: _ => Task.CompletedTask);

        item.SetAlternatives(
        [
            new EmailProjectAlternativeOption(5, "1", IsDefault: true),
            new EmailProjectAlternativeOption(6, "2", IsDefault: false),
        ]);

        item.ApplyTag(projectFileId: 100, projectFileTitle: "תוכנית", projectAlternativeId: null);

        Assert.True(item.IsTagged);
        Assert.Equal(5, item.SelectedAlternativeId);
    }
}
