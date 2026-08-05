using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.Diagnostics;

public sealed class WorkstationCrashReportWindow : Window
{
    public WorkstationCrashReportWindow(WorkstationCrashReportViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "דוח קריסות תחנה";
        Width = 1080;
        Height = 700;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new WorkstationCrashReportView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
