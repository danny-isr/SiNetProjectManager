using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Acc;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Aggregates legacy and new-system ACC background work counters for close-dialog UX.
/// </summary>
public static class AccBackgroundWorkMonitor
{
    private static event Action<int>? _totalActiveCountChanged;
    private static bool _workbenchSubscribed;

    public static int TotalActiveCount =>
        EmailManagementViewModel.ActiveUploadCount + GetWorkbenchActiveCount();

    public static event Action<int>? TotalActiveCountChanged
    {
        add
        {
            if (value is null)
            {
                return;
            }

            _totalActiveCountChanged += value;
            EmailManagementViewModel.ActiveUploadsChanged += OnLegacyChanged;
            EnsureWorkbenchSubscription();
            value(TotalActiveCount);
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            _totalActiveCountChanged -= value;
            EmailManagementViewModel.ActiveUploadsChanged -= OnLegacyChanged;
        }
    }

    public static bool HasActiveUploads => TotalActiveCount > 0;

    private static IEmailAccBackgroundWorkTracker? WorkbenchTracker =>
        App.ServiceProvider?.GetService<IEmailAccBackgroundWorkTracker>();

    private static int GetWorkbenchActiveCount() => WorkbenchTracker?.ActiveCount ?? 0;

    private static void EnsureWorkbenchSubscription()
    {
        if (_workbenchSubscribed)
        {
            return;
        }

        if (WorkbenchTracker is not { } tracker)
        {
            return;
        }

        tracker.ActiveCountChanged += OnWorkbenchChanged;
        _workbenchSubscribed = true;
    }

    private static void OnLegacyChanged(int _) => RaiseTotalChanged();

    private static void OnWorkbenchChanged(int _) => RaiseTotalChanged();

    private static void RaiseTotalChanged() =>
        _totalActiveCountChanged?.Invoke(TotalActiveCount);
}
