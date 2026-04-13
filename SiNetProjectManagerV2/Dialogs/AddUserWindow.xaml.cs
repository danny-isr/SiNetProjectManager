using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using SiNetSQL.MVVM;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Dialogs
{
    /// <summary>
    /// Window for adding new users to the SIUser table.
    /// Only accessible to Administrators (Role = Administrator).
    /// </summary>
    public partial class AddUserWindow : Window
    {
        private readonly AddUserViewModel _viewModel;

        public AddUserWindow()
        {
            InitializeComponent();

            // Resolve ViewModel from DI container
            _viewModel = App.ServiceProvider.GetRequiredService<AddUserViewModel>();

            // Inject the AD user loader delegate (bridges WPF → ViewModel)
            _viewModel.AdUserLoader = async ct =>
            {
                var adUsers = await ActiveDirectoryService.GetDomainUsersAsync(ct);
                return adUsers
                    .Select(u => new AddUserViewModel.AdUserDto(u.DisplayName, u.DomainLoginName, u.Email))
                    .ToList();
            };

            DataContext = _viewModel;

            // Subscribe to close request from ViewModel
            _viewModel.RequestClose += OnRequestClose;

            Closed += (s, e) =>
            {
                // Unsubscribe to prevent memory leaks
                _viewModel.RequestClose -= OnRequestClose;
            };
        }

        private void OnRequestClose(bool dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }
    }
}
