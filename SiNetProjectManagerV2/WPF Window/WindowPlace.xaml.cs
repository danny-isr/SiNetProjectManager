using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using System.Windows;

namespace WpfSiData.WPF_Window
{
    public partial class WindowPlace : Window
    {
        private readonly PlaceViewModel _viewModel;

        public WindowPlace()
        {
            InitializeComponent();

            // Resolve ViewModel from DI container
            _viewModel = App.ServiceProvider.GetRequiredService<PlaceViewModel>();
            DataContext = _viewModel;
        }

        public Place? SelectedPlace
        {
            get
            {
                if (DataContext is PlaceViewModel vm)
                    return vm.SelectedPlace;
                return null;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveChanges();

            if (_viewModel.SelectedPlace != null && _viewModel.SelectedPlace.InUse != true)
            {
                MessageBox.Show("לא ניתן לבחור מקום שאינו פעיל.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
