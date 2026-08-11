using System.IO;
using SiNet.App.Wpf.Surfaces.Email.Detail;
using SiNet.Application.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class UnifiedAttachmentStripTests
{
    [Fact]
    public void ShowAlternativeSelector_true_when_single_real_alternative()
    {
        var item = new EmailDetailAttachmentItem(
            inboxAttachmentId: 10,
            fileName: "a.dwg",
            kind: "DWG",
            size: "1 KB",
            isTaggable: true,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);

        item.SetAlternatives(
        [
            new EmailProjectAlternativeOption(5, "1", IsDefault: true),
        ]);

        Assert.True(item.ShowAlternativeSelector);
        Assert.Contains(item.AvailableAlternatives, a => a.IsCreateNew);
        Assert.Equal(2, item.AvailableAlternatives.Count);
    }

    [Fact]
    public void Strip_xaml_has_single_attachments_list_without_tagging_collection()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailAttachmentStripView.xaml");
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailAttachmentStripViewModel.cs");

        Assert.DoesNotContain("TaggingAttachments", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TaggingAttachments", vm, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Attachments}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowAlternativeSelector", xaml, StringComparison.Ordinal);
        Assert.Contains("TagCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateNew_sentinel_constant_is_negative()
    {
        Assert.Equal(-1, EmailProjectAlternativeOption.CreateNewId);
        Assert.True(EmailProjectAlternativeOption.CreateNewSentinel.IsCreateNew);
    }

    [Fact]
    public void AlternativeSelectionChanged_always_invokes_create_for_CreateNewId()
    {
        var code = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailAttachmentStripView.xaml.cs");

        Assert.Contains("CreateNewId", code, StringComparison.Ordinal);
        Assert.Contains("AlternativeChangedCommand.Execute", code, StringComparison.Ordinal);
        // Must not early-return before CreateNew handling when SelectedAlternativeId already equals -1.
        var createIdx = code.IndexOf("CreateNewId", StringComparison.Ordinal);
        var earlyReturnIdx = code.IndexOf(
            "if (item.SelectedAlternativeId == selectedId)",
            StringComparison.Ordinal);
        Assert.True(createIdx > 0);
        Assert.True(earlyReturnIdx > createIdx);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = FindRepoRoot();
        return File.ReadAllText(Path.Combine(dir, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "SiNetProjectManager_GitHub", "src")))
            {
                return Path.Combine(dir, "SiNetProjectManager_GitHub");
            }

            if (Directory.Exists(Path.Combine(dir, "src", "SiNet.Application")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
