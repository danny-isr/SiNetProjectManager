using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Billing;

/// <summary>
/// Read-only Billing Control Center host. Production path: New Shell <c>כספים → מרכז חיובים</c>.
/// </summary>
public sealed class BillingDashboardWindow : Window
{
    public BillingDashboardWindow(BillingDashboardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "מרכז חיובים";
        Width = 1400;
        Height = 860;
        MinWidth = 1100;
        MinHeight = 640;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new BillingDashboardView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
        Closing += (_, e) =>
        {
            if (!viewModel.TryLeaveUnsavedPreparationEdits())
                e.Cancel = true;
        };
    }
}
