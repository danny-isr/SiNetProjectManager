using SiNetSQL.MVVM;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SiNetProjectManagerV2.App;

namespace SiNetProjectManagerV2.WPFUserControl
{
    /// <summary>
    /// Interaction logic for ProjectFolderTreeView.xaml.
    /// Hosts the folder tree (left) and the reusable ProjectFileTreeControl (right).
    /// 
    /// Standalone usage (full project selector):
    ///   &lt;local:ProjectFolderTreeView /&gt;
    /// 
    /// Workflow usage (pre-selected project, specific folder):
    ///   &lt;local:ProjectFolderTreeView InitialProjectId="1234" InitialFolderTitle="הגשות" /&gt;
    /// </summary>
    public partial class ProjectFolderTreeView : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty InitialProjectIdProperty = DependencyProperty.Register(
            nameof(InitialProjectId),
            typeof(int?),
            typeof(ProjectFolderTreeView),
            new PropertyMetadata(null));

        /// <summary>
        /// When set, auto-selects this project and hides the project selector bar.
        /// </summary>
        public int? InitialProjectId
        {
            get => (int?)GetValue(InitialProjectIdProperty);
            set => SetValue(InitialProjectIdProperty, value);
        }

        public static readonly DependencyProperty InitialFolderTitleProperty = DependencyProperty.Register(
            nameof(InitialFolderTitle),
            typeof(string),
            typeof(ProjectFolderTreeView),
            new PropertyMetadata(null));

        /// <summary>
        /// When set together with InitialProjectId, auto-selects this folder after the project loads.
        /// </summary>
        public string? InitialFolderTitle
        {
            get => (string?)GetValue(InitialFolderTitleProperty);
            set => SetValue(InitialFolderTitleProperty, value);
        }

        #endregion

        public ProjectFolderTreeView()
        {
            InitializeComponent();

            // Defer VM creation to Loaded so DependencyProperties are populated from XAML
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            // Only create VM if not already set (allows external injection via DataContext)
            if (DataContext is ProjectFolderTreeViewModel)
                return;

            var dialogs = App.DialogServiceLocator.Instance ?? new Services.DialogService();
            var vm = new ProjectFolderTreeViewModel(dialogs)
            {
                InitialProjectId = InitialProjectId,
                InitialFolderTitle = InitialFolderTitle
            };
            DataContext = vm;
        }

        // Mouse wheel scrolling for folders ScrollViewer
        private void FoldersScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ProjectFolderTreeViewModel viewModel && e.NewValue is ProjectFolderNode node)
            {
                viewModel.SelectedNode = node;
            }
        }
    }
}

