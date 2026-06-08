using SiNetSQL.MVVM;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SiNetProjectManagerV2.WPF_Window
{
    /// <summary>
    /// Interaction logic for AlternativeNameWindow.xaml
    /// </summary>
    public partial class AlternativeNameWindow : Window
    {
        public AlternativeNameWindow(AlternativeNameViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Force focus to the button to trigger standard validation
            if (sender is UIElement element)
            {
                element.Focus();
            }

            // Force all bindings for textboxes and date pickers to update immediately.
            // This is required because if the user presses Enter to submit, focus is not lost
            // from the active editor, and setting DialogResult closes the window before focus-loss commits run.
            foreach (var dp in FindVisualChildren<DatePicker>(this))
            {
                var binding = BindingOperations.GetBindingExpression(dp, DatePicker.SelectedDateProperty);
                binding?.UpdateSource();
            }
            foreach (var tb in FindVisualChildren<TextBox>(this))
            {
                var binding = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                binding?.UpdateSource();
            }

            if (DataContext is AlternativeNameViewModel vm && vm.IsOkEnabled)
            {
                vm.DialogResult = true;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AlternativeNameViewModel vm)
                vm.DialogResult = false;
            DialogResult = false;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (var childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
}
