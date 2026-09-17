using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailProjectLabelMergeTests
{
    private static readonly ProjectLabelEntry Source = new(
        "src",
        $"{EmailGmailLabelNames.RootLabel}/ישן/(3070)ישן",
        3070,
        "(3070)ישן",
        $"{EmailGmailLabelNames.RootLabel}/ישן");

    private static readonly ProjectLabelEntry Target = new(
        "tgt",
        $"{EmailGmailLabelNames.RootLabel}/יבנה/(3070)חדש",
        3070,
        "(3070)חדש",
        $"{EmailGmailLabelNames.RootLabel}/יבנה");

    [Fact]
    public async Task Merge_attaches_all_source_messages_then_deletes_source()
    {
        var ops = new FakeMergeOps();
        ops.ByLabel["src"] = ["m1", "m2"];
        ops.ByLabel["tgt"] = ["m3"];

        var result = await GmailProjectLabelMerge.ExecuteAsync(ops, Source, Target);

        Assert.True(result.Succeeded);
        Assert.True(result.SourceDeleted);
        Assert.Equal(2, result.TransferredCount);
        Assert.Contains("tgt", ops.Attached["m1"]);
        Assert.Contains("tgt", ops.Attached["m2"]);
        Assert.Contains("src", ops.Deleted);
        Assert.False(ops.ByLabel.ContainsKey("src"));
        Assert.Equal(3, ops.ByLabel["tgt"].Count);
    }

    [Fact]
    public async Task Message_already_on_target_is_idempotent()
    {
        var ops = new FakeMergeOps();
        ops.ByLabel["src"] = ["m1", "m2"];
        ops.ByLabel["tgt"] = ["m1"];

        var result = await GmailProjectLabelMerge.ExecuteAsync(ops, Source, Target);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AlreadyHadTargetCount);
        Assert.Equal(1, result.TransferredCount);
        Assert.True(result.SourceDeleted);
    }

    [Fact]
    public async Task Partial_attach_failure_does_not_delete_source()
    {
        var ops = new FakeMergeOps { FailOnMessageId = "m2" };
        ops.ByLabel["src"] = ["m1", "m2"];
        ops.ByLabel["tgt"] = [];

        var result = await GmailProjectLabelMerge.ExecuteAsync(ops, Source, Target);

        Assert.False(result.Succeeded);
        Assert.True(result.IsPartial);
        Assert.False(result.SourceDeleted);
        Assert.Empty(ops.Deleted);
        Assert.True(ops.ByLabel.ContainsKey("src"));
    }

    [Fact]
    public async Task Verify_failure_does_not_delete_source()
    {
        var ops = new FakeMergeOps { HideFromTargetAfterAttach = "m1" };
        ops.ByLabel["src"] = ["m1"];
        ops.ByLabel["tgt"] = [];

        var result = await GmailProjectLabelMerge.ExecuteAsync(ops, Source, Target);

        Assert.False(result.Succeeded);
        Assert.False(result.SourceDeleted);
        Assert.Empty(ops.Deleted);
    }

    [Fact]
    public async Task Different_project_numbers_are_rejected()
    {
        var other = Target with { ProjectNumber = 1844, LabelId = "other" };
        var result = await GmailProjectLabelMerge.ExecuteAsync(new FakeMergeOps(), Source, other);

        Assert.False(result.Succeeded);
        Assert.False(result.SourceDeleted);
        Assert.Contains("פרויקטים שונים", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_label_id_is_rejected()
    {
        var error = GmailProjectLabelMerge.Validate(Source, Source);
        Assert.Contains("עצמה", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_merge_index_has_one_label()
    {
        var remaining = new[]
        {
            ("tgt", Target.FullPath),
        };
        var index = GmailProjectLabelIndex.Build(remaining, EmailGmailLabelNames.RootLabel, "s", 2);
        Assert.Single(index.Find(3070));
        Assert.Equal("tgt", index.Find(3070)[0].LabelId);
        await Task.CompletedTask;
    }

    private sealed class FakeMergeOps : IGmailProjectLabelMergeOperations
    {
        public Dictionary<string, List<string>> ByLabel { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Attached { get; } = new(StringComparer.Ordinal);
        public List<string> Deleted { get; } = [];
        public string? FailOnMessageId { get; init; }
        public string? HideFromTargetAfterAttach { get; init; }

        public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
            string labelId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(
                ByLabel.TryGetValue(labelId, out var ids) ? ids.ToArray() : []);

        public Task AttachProjectLabelAsync(
            string gmailMessageId,
            string projectLabelId,
            CancellationToken cancellationToken)
        {
            if (string.Equals(gmailMessageId, FailOnMessageId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("attach failed");
            }

            if (!Attached.TryGetValue(gmailMessageId, out var list))
            {
                list = [];
                Attached[gmailMessageId] = list;
            }

            list.Add(projectLabelId);
            if (!string.Equals(gmailMessageId, HideFromTargetAfterAttach, StringComparison.Ordinal))
            {
                if (!ByLabel.TryGetValue(projectLabelId, out var target))
                {
                    target = [];
                    ByLabel[projectLabelId] = target;
                }

                if (!target.Contains(gmailMessageId, StringComparer.Ordinal))
                {
                    target.Add(gmailMessageId);
                }
            }

            return Task.CompletedTask;
        }

        public Task DeleteLabelAsync(string labelId, CancellationToken cancellationToken)
        {
            Deleted.Add(labelId);
            ByLabel.Remove(labelId);
            return Task.CompletedTask;
        }
    }
}
