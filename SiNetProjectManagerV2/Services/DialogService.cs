namespace SiNetProjectManagerV2.Services
{
    // WPF project — must reference core/VM project and PresentationFramework.dll
    using System;
    using System.Windows;
    using Serilog;
    using SiNetSQL.MVVM; // ensure we implement the core interface used by view models
    using SiNetProjectManagerV2.WPF_Window;

    public class DialogService : IDialogService
    {
        public void ShowDialog(object viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            Window window;

            // Explicit mapping for rename dialog
            if (viewModel is RenameProjectDialogViewModel renameVm)
            {
                window = new RenameProjectWindow(renameVm);
            }
            else
            {
                var vmType = viewModel.GetType().FullName;
                if (string.IsNullOrEmpty(vmType))
                    throw new InvalidOperationException("Cannot resolve view model type name.");
                var viewName = vmType.Replace("SiNetSQL.MVVM.", "SiNetProjectManagerV2.WPF_Window.")
                                      .Replace("ViewModel", "Window");

                var viewType = Type.GetType(viewName);
                if (viewType == null)
                {
                    Log.Error("DialogService: No view found for {VmType} -> {ViewName}", vmType, viewName);
                    throw new InvalidOperationException($"No view found for {vmType}");
                }

                var ctor = viewType.GetConstructor(new[] { viewModel.GetType() });
                if (ctor != null)
                {
                    window = (Window)ctor.Invoke(new[] { viewModel });
                }
                else
                {
                    window = (Window)Activator.CreateInstance(viewType)!;
                    window.DataContext = viewModel;
                }
            }

            try
            {
                // Ensure we have an owner so dialog is modal and visible on top
                if (window.Owner == null && Application.Current?.MainWindow != null && window != Application.Current.MainWindow)
                {
                    window.Owner = Application.Current.MainWindow;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error showing dialog for {VmType}", viewModel.GetType().Name);
                throw;
            }
        }

        // optional legacy generic overload (not used currently)
        public T ShowDialog<T>(string dialogName, object parameter)
        {
            var viewType = Type.GetType(dialogName);
            if (viewType == null) throw new InvalidOperationException($"Dialog '{dialogName}' not found");
            if (!typeof(Window).IsAssignableFrom(viewType)) throw new InvalidOperationException($"Type '{dialogName}' is not a Window");
            var window = (Window)Activator.CreateInstance(viewType)!;
            window.DataContext = parameter;
            if (window.Owner == null && Application.Current?.MainWindow != null && window != Application.Current.MainWindow)
            {
                window.Owner = Application.Current.MainWindow;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            window.ShowDialog();
            return default!;
        }
    }
}
