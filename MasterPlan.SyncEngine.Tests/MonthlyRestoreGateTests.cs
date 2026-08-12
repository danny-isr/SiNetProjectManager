using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class MonthlyRestoreGateTests
{
    [Fact]
    public void When_no_previous_stamp_then_any_backup_is_allowed()
    {
        Assert.True(MonthlyRestoreGate.IsNewerThanLastRestore(
            new DateTime(2026, 8, 1, 10, 0, 0),
            lastSuccessfulRestore: null));
    }

    [Fact]
    public void When_backup_is_later_than_last_restore_then_allowed()
    {
        Assert.True(MonthlyRestoreGate.IsNewerThanLastRestore(
            new DateTime(2026, 8, 12, 8, 0, 0),
            new DateTime(2026, 7, 12, 8, 0, 0)));
    }

    [Fact]
    public void When_backup_equals_last_restore_then_refused()
    {
        var stamp = new DateTime(2026, 8, 1, 10, 0, 0);
        Assert.False(MonthlyRestoreGate.IsNewerThanLastRestore(stamp, stamp));
    }

    [Fact]
    public void When_backup_is_older_than_last_restore_then_refused()
    {
        Assert.False(MonthlyRestoreGate.IsNewerThanLastRestore(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 8, 1)));
    }
}
