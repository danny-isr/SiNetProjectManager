using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class MasterPlanReportSqlSourceResolverTests
{
    [Fact]
    public void When_both_configured_then_replica_wins()
    {
        var settings = new MasterPlanEmployeeConnectionSettings
        {
            ReplicaDatabase = "replica-cs",
            MasterPlanDatabase = "live-mp-cs",
        };

        var source = MasterPlanReportSqlSourceResolver.Resolve(settings);

        Assert.Equal(MasterPlanReportSqlSourceKind.Replica, source.Kind);
        Assert.Equal("replica-cs", source.ConnectionString);
    }

    [Fact]
    public void When_only_replica_then_replica()
    {
        var settings = new MasterPlanEmployeeConnectionSettings
        {
            ReplicaDatabase = "replica-cs",
        };

        var source = MasterPlanReportSqlSourceResolver.Resolve(settings);

        Assert.Equal(MasterPlanReportSqlSourceKind.Replica, source.Kind);
    }

    [Fact]
    public void When_only_live_mp_then_last_resort_live()
    {
        var settings = new MasterPlanEmployeeConnectionSettings
        {
            MasterPlanDatabase = "live-mp-cs",
        };

        var source = MasterPlanReportSqlSourceResolver.Resolve(settings);

        Assert.Equal(MasterPlanReportSqlSourceKind.LiveMasterPlan, source.Kind);
        Assert.Equal("live-mp-cs", source.ConnectionString);
    }

    [Fact]
    public void When_neither_configured_then_throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => MasterPlanReportSqlSourceResolver.Resolve(new MasterPlanEmployeeConnectionSettings()));
    }

    [Fact]
    public void RequireReplica_rejects_live_only()
    {
        var settings = new MasterPlanEmployeeConnectionSettings
        {
            MasterPlanDatabase = "live-mp-cs",
        };

        Assert.Throws<InvalidOperationException>(
            () => MasterPlanReportSqlSourceResolver.RequireReplica(settings));
    }

    [Fact]
    public void RequireReplica_accepts_replica_even_when_live_is_also_set()
    {
        var settings = new MasterPlanEmployeeConnectionSettings
        {
            ReplicaDatabase = "replica-cs",
            MasterPlanDatabase = "live-mp-cs",
        };

        var source = MasterPlanReportSqlSourceResolver.RequireReplica(settings);

        Assert.Equal(MasterPlanReportSqlSourceKind.Replica, source.Kind);
        Assert.Equal("replica-cs", source.ConnectionString);
    }
}
