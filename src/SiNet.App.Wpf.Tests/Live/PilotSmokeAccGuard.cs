using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Autodesk.Metadata;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Last line of defence for the ACC layer of the L4W write smoke.
/// <para>
/// The place-name convention in <c>docs/ENVIRONMENTS.md</c> §5.1 is process-only, and it does not
/// govern the Office Inbox target at all (§5.1.1). This guard turns "we intended to write to a
/// disposable project" into an enforced invariant by decorating the two ports that actually mutate
/// ACC and refusing any project id that was not explicitly allowlisted for this run.
/// </para>
/// <para>
/// The allowlist starts <b>empty</b>. Ids are added only after the harness has created or resolved
/// the specific disposable project, so an unexpected target fails loudly instead of uploading.
/// </para>
/// </summary>
internal sealed class PilotSmokeAccGuard
{
    private readonly Dictionary<string, string> _allowedProjectIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _blockedAttempts = [];
    private readonly Lock _sync = new();

    /// <summary>
    /// Allows one ACC project id for the remainder of the run. Called only after the harness has
    /// verified the project is the disposable smoke target. <paramref name="why"/> is recorded so the
    /// evidence file states on what grounds each id was trusted.
    /// </summary>
    public void Allow(string accProjectId, string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(why);

        lock (_sync)
        {
            _allowedProjectIds[accProjectId.Trim()] = why;
        }
    }

    /// <summary>Allowed ids with the reason each one was admitted.</summary>
    public IReadOnlyList<string> AllowedProjectIds
    {
        get
        {
            lock (_sync)
            {
                return [.. _allowedProjectIds.Select(p => $"{p.Key} ({p.Value})")];
            }
        }
    }

    /// <summary>Every write this guard refused. Must be empty at the end of a clean run.</summary>
    public IReadOnlyList<string> BlockedAttempts
    {
        get
        {
            lock (_sync)
            {
                return [.. _blockedAttempts];
            }
        }
    }

    /// <summary>
    /// Replaces <see cref="IAccFileUploadService"/> and <see cref="IAccItemMetadataService"/> with
    /// guarded decorators over whatever the host graph registered.
    /// </summary>
    public void Decorate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Decorate<IAccFileUploadService>(services, (inner, guard) => new GuardedUpload(inner, guard));
        Decorate<IAccItemMetadataService>(services, (inner, guard) => new GuardedMetadata(inner, guard));
    }

    /// <summary>
    /// Asserts the graph exposes no ACC mutation port at all. Used when the ACC tier is off, so a
    /// SQL-or-Gmail-only run cannot reach ACC by an unnoticed code path.
    /// </summary>
    public static void AssertNoAccWritePortsRegistered(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var present = services
            .Where(d => d.ServiceType == typeof(IAccFileUploadService)
                     || d.ServiceType == typeof(IAccItemMetadataService))
            .Select(d => d.ServiceType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (present.Count > 0)
        {
            throw new InvalidOperationException(
                "The ACC tier is disabled but the service graph still exposes ACC write ports: "
                + string.Join(", ", present)
                + ". Remove the ACC modules from the smoke graph or enable the ACC tier explicitly.");
        }
    }

    private void Decorate<TService>(
        IServiceCollection services,
        Func<TService, PilotSmokeAccGuard, TService> wrap)
        where TService : class
    {
        var existing = services.Where(d => d.ServiceType == typeof(TService)).ToList();
        if (existing.Count == 0)
        {
            throw new InvalidOperationException(
                $"The ACC tier requires {typeof(TService).Name} to be registered before guarding it.");
        }

        foreach (var descriptor in existing)
        {
            services.Remove(descriptor);
        }

        var inner = existing[^1];
        services.Add(new ServiceDescriptor(
            typeof(TService),
            sp => wrap((TService)Instantiate(sp, inner), this),
            inner.Lifetime));
    }

    private static object Instantiate(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(sp);
        }

        return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }

    private void EnsureAllowed(string accProjectId, string operation, string? detail)
    {
        lock (_sync)
        {
            if (_allowedProjectIds.ContainsKey(accProjectId?.Trim() ?? string.Empty))
            {
                return;
            }

            var attempt =
                $"{operation} on ACC project '{accProjectId ?? "<null>"}'"
                + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})");
            _blockedAttempts.Add(attempt);

            throw new InvalidOperationException(
                $"PilotSmokeAccGuard blocked {attempt}. Allowed ids for this run: "
                + (_allowedProjectIds.Count == 0
                    ? "<none yet>"
                    : string.Join(", ", _allowedProjectIds.Keys))
                + ". See docs/TEST_STRATEGY.md §4W.2.");
        }
    }

    private sealed class GuardedUpload(IAccFileUploadService inner, PilotSmokeAccGuard guard)
        : IAccFileUploadService
    {
        public Task<AccFileUploadResult> UploadAsync(
            AccFileUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            guard.EnsureAllowed(request.ProjectId, "ACC upload", request.DisplayName);
            return inner.UploadAsync(request, cancellationToken);
        }
    }

    private sealed class GuardedMetadata(IAccItemMetadataService inner, PilotSmokeAccGuard guard)
        : IAccItemMetadataService
    {
        public ValueTask<AccItemMetadataReadResult> ReadAttributesAsync(
            string accProjectId,
            string itemId,
            string? fileNameForLogging,
            CancellationToken cancellationToken)
        {
            // Reads are harmless, but keeping them guarded means an unexpected project id surfaces
            // during the read that normally precedes a write rather than after the mutation.
            guard.EnsureAllowed(accProjectId, "ACC metadata read", fileNameForLogging);
            return inner.ReadAttributesAsync(accProjectId, itemId, fileNameForLogging, cancellationToken);
        }

        public ValueTask<AccItemMetadataResult> WriteAttributesAsync(
            string accProjectId,
            string accFolderId,
            string versionId,
            string itemId,
            IReadOnlyDictionary<string, string?> attributes,
            string? fileNameForLogging,
            CancellationToken cancellationToken)
        {
            guard.EnsureAllowed(accProjectId, "ACC metadata write", fileNameForLogging);
            return inner.WriteAttributesAsync(
                accProjectId, accFolderId, versionId, itemId, attributes, fileNameForLogging, cancellationToken);
        }
    }
}
