using System.Windows;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

public sealed class ProjectCreateDialogWindow : Window
{
    private readonly ProjectCreateDialogViewModel _viewModel;
    private readonly IPlaceCatalogService _places;
    private readonly ICompanyCatalogService _companies;

    public ProjectCreateDialogWindow(
        ProjectCreateDialogViewModel viewModel,
        IPlaceCatalogService places,
        ICompanyCatalogService companies)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _places = places ?? throw new ArgumentNullException(nameof(places));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));

        Title = "פתיחת פרויקט חדש";
        Width = 680;
        Height = 780;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 560;
        MinHeight = 640;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new ProjectCreateDialogView { DataContext = _viewModel };

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.RequestPlacePicker += OnRequestPlacePickerAsync;
        _viewModel.RequestCompanyPicker += OnRequestCompanyPickerAsync;
        Closed += (_, _) =>
        {
            _viewModel.RequestClose -= OnRequestClose;
            _viewModel.RequestPlacePicker -= OnRequestPlacePickerAsync;
            _viewModel.RequestCompanyPicker -= OnRequestCompanyPickerAsync;
        };
        Loaded += async (_, _) =>
        {
            try
            {
                await _viewModel.InitializeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppErrorReporter.Report(ex, "ProjectCreateDialogWindow.OnLoaded");
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnRequestClose(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    private async Task<PlaceDto?> OnRequestPlacePickerAsync()
    {
        var pickerVm = new PlacePickerDialogViewModel(_places);
        var window = new Window
        {
            Title = "בחר מקום",
            Owner = this,
            Width = 420,
            Height = 520,
            FlowDirection = FlowDirection.RightToLeft,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new PlacePickerDialogView { DataContext = pickerVm },
        };
        ThemeWindowChrome.ApplyThemedWindowBackground(window);
        pickerVm.RequestClose += result =>
        {
            window.DialogResult = result;
            window.Close();
        };
        await pickerVm.InitializeAsync().ConfigureAwait(true);
        return window.ShowDialog() == true ? pickerVm.SelectedPlace : null;
    }

    private async Task<(CompanyDto? Company, ContactDto? Contact)> OnRequestCompanyPickerAsync()
    {
        var pickerVm = new CompanyContactPickerDialogViewModel(_companies);
        var window = new Window
        {
            Title = "בחר חברה ואיש קשר",
            Owner = this,
            Width = 720,
            Height = 560,
            FlowDirection = FlowDirection.RightToLeft,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new CompanyContactPickerDialogView { DataContext = pickerVm },
        };
        ThemeWindowChrome.ApplyThemedWindowBackground(window);
        pickerVm.RequestClose += result =>
        {
            window.DialogResult = result;
            window.Close();
        };
        await pickerVm.InitializeAsync().ConfigureAwait(true);
        return window.ShowDialog() == true
            ? (pickerVm.SelectedCompany, pickerVm.SelectedContact)
            : (null, null);
    }
}
