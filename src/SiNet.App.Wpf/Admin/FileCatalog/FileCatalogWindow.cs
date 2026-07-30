using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.FileCatalog;

/// <summary>Native New System host for the global file/folder catalog admin («ניהול קבצים»).</summary>
public sealed class FileCatalogWindow : Window
{
    public FileCatalogWindow(FileCatalogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Title = "ניהול קבצים";
        Width = 1200;
        Height = 720;
        MinWidth = 900;
        MinHeight = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new FileCatalogView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
