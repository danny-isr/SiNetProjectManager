using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Actions;
using SiNet.Application.Notifications;
using SiNet.Infrastructure.Sql.DependencyInjection;
using SiNetSQL.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Actions;

/// <summary>
/// Phase 5b: verifies the native <c>SendNotification</c> handler parses the transition config and
/// delegates to <see cref="INotificationDeliveryService"/>, mapping the delivery outcome to the
/// process-action result (failure blocks a transition; nothing-to-deliver still completes).
/// </summary>
public sealed class SendNotificationDeliveryTests
{
    [Fact]
    public async Task Dispatch_parses_template_and_recipients_and_delivers()
    {
        var recorder = new RecordingDelivery(NotificationDeliveryResult.Delivered("test"));
        await using var provider = BuildProvider(recorder);
        var actions = provider.GetRequiredService<IProcessActionService>();

        var result = await actions.DispatchAsync(
            new ActionExecutionCommand(
                ProcessActionCodes.SendNotification,
                ProjectId: 7,
                WorkflowInstanceId: 9,
                UserId: 3,
                Data: new Dictionary<string, object?>
                {
                    [ActionExecutionDataKeys.ConfigJson] = "{\"template\":\"MaterialReady\",\"to\":\"a@b.com, c@d.com\"}",
                }),
            CancellationToken.None);

        Assert.Equal(ActionExecutionStatus.Completed, result.Status);
        Assert.NotNull(recorder.LastRequest);
        Assert.Equal("MaterialReady", recorder.LastRequest!.Template);
        Assert.Equal(new[] { "a@b.com", "c@d.com" }, recorder.LastRequest.Recipients);
        Assert.Equal(7, recorder.LastRequest.ProjectId);
        Assert.Equal(9, recorder.LastRequest.WorkflowInstanceId);
    }

    [Fact]
    public async Task Dispatch_parses_recipients_array()
    {
        var recorder = new RecordingDelivery(NotificationDeliveryResult.Delivered("test"));
        await using var provider = BuildProvider(recorder);
        var actions = provider.GetRequiredService<IProcessActionService>();

        await actions.DispatchAsync(
            new ActionExecutionCommand(
                ProcessActionCodes.SendNotification,
                Data: new Dictionary<string, object?>
                {
                    [ActionExecutionDataKeys.ConfigJson] = "{\"to\":[\"x@y.com\",\"z@y.com\"]}",
                }),
            CancellationToken.None);

        Assert.Equal(new[] { "x@y.com", "z@y.com" }, recorder.LastRequest!.Recipients);
    }

    [Fact]
    public async Task Dispatch_maps_delivery_failure_to_failed_result()
    {
        var recorder = new RecordingDelivery(NotificationDeliveryResult.Failed("test", "channel unavailable"));
        await using var provider = BuildProvider(recorder);
        var actions = provider.GetRequiredService<IProcessActionService>();

        var result = await actions.DispatchAsync(
            new ActionExecutionCommand(
                ProcessActionCodes.SendNotification,
                Data: new Dictionary<string, object?>
                {
                    [ActionExecutionDataKeys.ConfigJson] = "{\"template\":\"X\",\"to\":\"a@b.com\"}",
                }),
            CancellationToken.None);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Contains("channel unavailable", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_with_no_config_completes_via_default_log_channel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(InMemoryOptions()));
        services.AddSiNetActionServices();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var actions = provider.GetRequiredService<IProcessActionService>();

        var result = await actions.DispatchAsync(
            new ActionExecutionCommand(ProcessActionCodes.SendNotification, ProjectId: 1, UserId: 2),
            CancellationToken.None);

        Assert.Equal(ActionExecutionStatus.Completed, result.Status);
    }

    private static ServiceProvider BuildProvider(INotificationDeliveryService delivery)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(InMemoryOptions()));
        services.AddSiNetActionServices();
        // Overrides the default log delivery: for a single service the last registration wins.
        services.AddSingleton(delivery);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static DbContextOptions<SiNetSQLDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private sealed class RecordingDelivery(NotificationDeliveryResult result) : INotificationDeliveryService
    {
        private readonly NotificationDeliveryResult _result = result;

        public NotificationDeliveryRequest? LastRequest { get; private set; }

        public ValueTask<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
