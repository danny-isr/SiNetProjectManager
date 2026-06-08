using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2;
using SiNetSQL.MVVM;
using System.Windows;
using System.Windows.Controls;
using WpfSiData.WPF_Window;

namespace WpfSiData.WPFUserControl
{
    /// <summary>
    /// Interaction logic for CreateProjectUserControl.xaml
    /// </summary>
    public partial class CreateProjectUserControl : UserControl
    {
        private readonly CreateProjectViewModel _viewModel;

        public CreateProjectUserControl(int? emailMessageId = null)
        {
            InitializeComponent();

            // Resolve ViewModel from DI container
            _viewModel = App.ServiceProvider.GetRequiredService<CreateProjectViewModel>();
            DataContext = _viewModel;

            if (emailMessageId.HasValue)
            {
                _viewModel.SetEmailContext(emailMessageId.Value);
            }
        }
        private async void OK_Click(object sender, RoutedEventArgs e)
        {
            if (await _viewModel.SaveChangesAsync())
            {
                _viewModel.Reload();
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var window = new WindowPlace();
            if (window.ShowDialog() == true)
            {
                var selectedPlace = window.SelectedPlace;
                if (selectedPlace != null && DataContext is CreateProjectViewModel viewModel)
                {
                    await viewModel.LoadPlacesAsync();
                    var freshPlace = viewModel.Places?.FirstOrDefault(p => p.Id == selectedPlace.Id);
                    if (freshPlace != null)
                        viewModel.SelectedPlace = freshPlace;
                }
            }
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            var window = new WindowCompany();
            if (window.ShowDialog() == true)
            {
                var selectedContact = window.SelectedContact;
                if (selectedContact != null && DataContext is CreateProjectViewModel viewModel)
                {
                    await viewModel.LoadCompanyAsync();
                    var companyId = selectedContact.CompanyId ?? selectedContact.Company?.Id;
                    var freshCompany = companyId != null ? viewModel.Companies?.FirstOrDefault(p => p.Id == companyId) : null;
                    if (freshCompany != null)
                        viewModel.SelectedCompany = freshCompany;
                    var freshContact = viewModel.Contacts?.FirstOrDefault(p => p.Id == selectedContact.Id);
                    if (freshContact != null)
                        viewModel.SelectedContact = freshContact;
                }
            }
        }
    }
}
