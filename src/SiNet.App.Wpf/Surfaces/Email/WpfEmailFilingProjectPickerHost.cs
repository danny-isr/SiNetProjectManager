using System.Windows;
using System.Windows.Controls;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Email.Detail;
using SiNet.Application.Projects;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Native WPF project picker for email filing — uses a local project context and never
/// mutates the app singleton <see cref="ICurrentProjectContext"/>.
/// </summary>
internal sealed class WpfEmailFilingProjectPickerHost(
    IProjectQueryService projectQuery,
    IProjectFilterOptionsService filterOptions,
    IAppSettingsService? appSettings = null) : IEmailFilingProjectPickerHost
{
    private readonly IProjectQueryService _projectQuery =
        projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
    private readonly IProjectFilterOptionsService _filterOptions =
        filterOptions ?? throw new ArgumentNullException(nameof(filterOptions));
    private readonly IAppSettingsService? _appSettings = appSettings;

    public bool IsAvailable => true;

    public Task<ProjectSummaryDto?> PickProjectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return Task.FromResult<ProjectSummaryDto?>(null);

        if (dispatcher.CheckAccess())
            return Task.FromResult(ShowDialog());

        return dispatcher.InvokeAsync(ShowDialog).Task;
    }

    private ProjectSummaryDto? ShowDialog()
    {
        var localContext = new InMemoryCurrentProjectContext();
        var selector = new ProjectSelectorViewModel(
            _projectQuery,
            _filterOptions,
            localContext,
            appSettings: _appSettings);
        _ = selector.InitializeAsync();

        var window = new Window
        {
            Title = "בחירת פרויקט לשיוך המייל",
            Width = 720,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FlowDirection = FlowDirection.RightToLeft,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    CreateButtons(out var okButton, out var cancelButton),
                    new ProjectSelectorView
                    {
                        DataContext = selector,
                        CompactMode = true,
                        Margin = new Thickness(0, 0, 0, 12),
                    },
                },
            },
        };

        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner)
            window.Owner = owner;

        ProjectSummaryDto? result = null;
        okButton.Click += (_, _) =>
        {
            result = localContext.CurrentProject ?? selector.SelectedProject;
            if (result is null)
            {
                MessageBox.Show(
                    window,
                    "יש לבחור פרויקט.",
                    "שיוך מייל",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            window.DialogResult = true;
            window.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };

        window.ShowDialog();
        selector.Dispose();
        return window.DialogResult == true ? result : null;
    }

    private static UIElement CreateButtons(out Button okButton, out Button cancelButton)
    {
        okButton = new Button
        {
            Content = "שייך",
            IsDefault = true,
            MinWidth = 90,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };
        cancelButton = new Button
        {
            Content = "ביטול",
            IsCancel = true,
            MinWidth = 70,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(okButton);
        panel.Children.Add(cancelButton);
        DockPanel.SetDock(panel, Dock.Bottom);
        return panel;
    }
}
