using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class RecoverFileNamingTests
{
    [Theory]
    [InlineData("plan.dwg", false)]
    [InlineData("plan_recover.dwg", true)]
    [InlineData("plan_recover000.dwg", true)]
    [InlineData("plan_recover001.dwg", true)]
    [InlineData("plan_RECOVER.dwg", true)]
    [InlineData("plan_recovery.dwg", false)]
    [InlineData("plan_recover1234.dwg", false)]
    public void IsRecoverFileName_matches_office_patterns(string name, bool expected)
        => Assert.Equal(expected, RecoverFileNaming.IsRecoverFileName(name));

    [Theory]
    [InlineData("plan_recover.dwg", "plan.dwg")]
    [InlineData("plan_recover000.dwg", "plan.dwg")]
    [InlineData("plan_recover001.dwg", "plan.dwg")]
    [InlineData("(1844)-A-1-1-1-name_recover.dwg", "(1844)-A-1-1-1-name.dwg")]
    public void TryGetPrimaryFileName_strips_recover_suffix(string recover, string primary)
    {
        Assert.True(RecoverFileNaming.TryGetPrimaryFileName(recover, out var result));
        Assert.Equal(primary, result);
    }

    [Fact]
    public void TryGetPrimaryFileName_false_for_non_recover()
        => Assert.False(RecoverFileNaming.TryGetPrimaryFileName("plan.dwg", out _));
}

public sealed class RecoverFileRelevanceTests
{
    private static readonly DateTime Primary = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Zero_byte_recover_is_never_actionable()
        => Assert.False(RecoverFileRelevance.IsActionableNewerThanPrimary(
            recoverLength: 0,
            recoverLastWrite: Primary.AddDays(1),
            primaryLastWrite: Primary));

    [Fact]
    public void Newer_non_empty_recover_is_actionable()
        => Assert.True(RecoverFileRelevance.IsActionableNewerThanPrimary(
            recoverLength: 100,
            recoverLastWrite: Primary.AddHours(1),
            primaryLastWrite: Primary));

    [Fact]
    public void Older_or_equal_recover_is_not_actionable()
    {
        Assert.False(RecoverFileRelevance.IsActionableNewerThanPrimary(100, Primary, Primary));
        Assert.False(RecoverFileRelevance.IsActionableNewerThanPrimary(100, Primary.AddHours(-1), Primary));
    }

    [Fact]
    public void Stale_delete_never_includes_orphans()
        => Assert.False(RecoverFileRelevance.IsEligibleForStaleDelete(
            hasPrimary: false,
            recoverLength: 100,
            recoverLastWrite: Primary.AddDays(-10),
            primaryLastWrite: Primary));

    [Fact]
    public void Stale_delete_with_zero_threshold_includes_recover_not_newer_than_primary()
    {
        Assert.True(RecoverFileRelevance.IsEligibleForStaleDelete(true, 100, Primary.AddHours(-1), Primary));
        Assert.True(RecoverFileRelevance.IsEligibleForStaleDelete(true, 100, Primary, Primary));
        Assert.False(RecoverFileRelevance.IsEligibleForStaleDelete(true, 100, Primary.AddHours(1), Primary));
    }

    [Fact]
    public void Zero_byte_paired_recover_is_always_eligible_for_stale_delete()
        => Assert.True(RecoverFileRelevance.IsEligibleForStaleDelete(
            hasPrimary: true,
            recoverLength: 0,
            recoverLastWrite: Primary.AddDays(5),
            primaryLastWrite: Primary));
}

public sealed class RecoverScanClassifierTests
{
    [Fact]
    public void Classifier_hides_stale_and_shows_only_best_newer_recover()
    {
        var primaryTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var roles = RecoverScanClassifier.Classify(
        [
            new("plan.dwg", 1000, primaryTime),
            new("plan_recover.dwg", 1000, primaryTime.AddHours(-2)),
            new("plan_recover001.dwg", 1000, primaryTime.AddHours(1)),
            new("plan_recover000.dwg", 0, primaryTime.AddHours(5)),
        ]);

        Assert.Equal(RecoverTreeRole.NotRecover, roles["plan.dwg"]);
        Assert.Equal(RecoverTreeRole.Hidden, roles["plan_recover.dwg"]);
        Assert.Equal(RecoverTreeRole.ActionableNewer, roles["plan_recover001.dwg"]);
        Assert.Equal(RecoverTreeRole.Hidden, roles["plan_recover000.dwg"]);
    }

    [Fact]
    public void Classifier_marks_orphan_recovers_without_primary()
    {
        var roles = RecoverScanClassifier.Classify(
        [
            new("lonely_recover.dwg", 100, DateTime.UtcNow),
            new("empty_recover.dwg", 0, DateTime.UtcNow),
        ]);

        Assert.Equal(RecoverTreeRole.Orphan, roles["lonely_recover.dwg"]);
        Assert.Equal(RecoverTreeRole.Hidden, roles["empty_recover.dwg"]);
    }
}
