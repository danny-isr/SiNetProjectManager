using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Background reconciler that propagates <see cref="SiNetSQL.Models.Siuser"/> changes
/// to every ACC project (<see cref="SiNetSQL.Models.ProjectAccMapping"/>).
/// <para>
/// Uses a bounded <see cref="Channel{T}"/> of capacity 1 so many rapid
/// enqueues coalesce into a single background pass. A single worker drains
/// the channel, enumerates all mappings, and calls
/// <see cref="IAccProjectProvisioningService.ReconcileProjectMembersAsync"/>
/// on each — which is idempotent (SKIP/ADD/UPGRADE per user).
/// </para>
/// </summary>
public sealed class AccMembershipReconciler : IAccMembershipReconciler, IDisposable
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory;
    private readonly IAccProjectProvisioningService _provisioning;
    private readonly Channel<byte> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public AccMembershipReconciler(
        IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
        IAccProjectProvisioningService provisioning)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));

        // Capacity 1 + DropWrite: coalesce bursts into one pending pass.
        _channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <inheritdoc/>
    public void EnqueueReconcileAll()
    {
        // TryWrite returns false if a pass is already pending — that's fine,
        // the existing pending pass will pick up our latest state.
        _ = _channel.Writer.TryWrite(0);
        AccBootstrapLog.Info("[AccReconciler] Reconciliation enqueued.");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // Drain: we don't care how many writes happened — one pass covers all.
                while (_channel.Reader.TryRead(out _)) { }

                // Small debounce so a burst of saves (e.g. multi-row edit) is batched.
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                await ReconcileAllAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            AccBootstrapLog.Error(ex, "[AccReconciler] Worker crashed");
        }
    }

    private async Task ReconcileAllAsync(CancellationToken ct)
    {
        List<string> accProjectIds;
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            accProjectIds = await db.ProjectAccMappings
                .AsNoTracking()
                .Where(m => m.AccProjectId != null && m.AccProjectId != "")
                .Select(m => m.AccProjectId!)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Error(ex, "[AccReconciler] Failed to load ProjectAccMappings");
            return;
        }

        if (accProjectIds.Count == 0)
        {
            AccBootstrapLog.Info("[AccReconciler] No ACC project mappings — nothing to reconcile.");
            return;
        }

        AccBootstrapLog.Info($"[AccReconciler] Reconciling members for {accProjectIds.Count} ACC project(s)...");
        int ok = 0, failed = 0;
        foreach (var accProjectId in accProjectIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _provisioning.ReconcileProjectMembersAsync(accProjectId, ct).ConfigureAwait(false);
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                AccBootstrapLog.Error(ex, $"[AccReconciler] Failed for AccProjectId={accProjectId}");
            }
        }

        AccBootstrapLog.Info($"[AccReconciler] Pass complete. ok={ok} failed={failed}");
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _channel.Writer.TryComplete(); } catch { }
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
