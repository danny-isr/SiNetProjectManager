using System;
using System.Threading;
using System.Threading.Tasks;
using SiNetSQL.FileIndex.Stores;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Application-side <see cref="IGoogleDriveServiceProvider"/> built on the
/// existing singleton <see cref="GoogleAuthService"/>. Performs one lazy
/// authentication on first use and caches the resulting
/// <see cref="GoogleDriveService"/> bound to the configured Shared Drive.
/// <para>
/// No fallback: when auth or configuration is unavailable, <see cref="TryGetAsync"/>
/// returns <c>null</c> and callers must surface an explicit failure (Gap 9
/// / Google Drive activation policy).
/// </para>
/// </summary>
public sealed class GoogleDriveServiceProvider : IGoogleDriveServiceProvider
{
    private readonly GoogleAuthService _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GoogleDriveService? _cached;

    public GoogleDriveServiceProvider(GoogleAuthService auth, GoogleDriveSettings settings)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public GoogleDriveSettings Settings { get; }

    public async Task<IGoogleDriveService?> TryGetAsync(CancellationToken ct = default)
    {
        if (!Settings.IsConfigured) return null;
        if (_cached is not null) return _cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null) return _cached;

            if (!_auth.IsAuthenticated)
            {
                var ok = await _auth.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
                if (!ok) return null;
            }

            var drive = _auth.DriveService;
            if (drive is null) return null;

            _cached = new GoogleDriveService(drive, Settings.SharedDriveId!);
            return _cached;
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
