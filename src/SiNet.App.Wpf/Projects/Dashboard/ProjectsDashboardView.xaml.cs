using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SiNet.Application.Projects;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Projects.Dashboard;

public partial class ProjectsDashboardView : UserControl
{
    public ProjectsDashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AdoptionDebugLog.Write(
                "ProjectsDashboardView.Loaded",
                $"dataContext={(DataContext is null ? "null" : DataContext.GetType().FullName)} isProjectsDashboardViewModel={DataContext is ProjectsDashboardViewModel}");
        };
    }

    private void OnAdoptExistingWorkflowClick(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as ProjectsDashboardViewModel;
        var command = vm?.AdoptExistingWorkflowCommand;
        AdoptionDebugLog.Write(
            "Button clicked",
            $"dataContext={(DataContext is null ? "null" : DataContext.GetType().FullName)} commandNull={command is null} canExecute={command?.CanExecute(null)} selectedProjectId={vm?.Selected?.ProjectId}");
    }

    private async void OnProjectsGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProjectsDashboardViewModel vm)
            await vm.OpenSelectedForCurrentRoleAsync();
    }

    private void OnProjectsGridSorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource) as ListCollectionView;
        if (view is null)
            return;

        var isNumberColumn = string.Equals(
                                 e.Column.SortMemberPath,
                                 nameof(ProjectsDashboardRowVm.ProjectNumberSortKey),
                                 StringComparison.Ordinal)
                             || string.Equals(
                                 e.Column.SortMemberPath,
                                 nameof(ProjectsDashboardRowVm.ProjectNumberValue),
                                 StringComparison.Ordinal)
                             || string.Equals(
                                 e.Column.SortMemberPath,
                                 nameof(ProjectsDashboardRowVm.ProjectNumber),
                                 StringComparison.Ordinal);

        if (!isNumberColumn)
        {
            view.CustomSort = null;
            return;
        }

        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        e.Column.SortDirection = direction;

        foreach (var column in grid.Columns)
        {
            if (!ReferenceEquals(column, e.Column))
                column.SortDirection = null;
        }

        view.CustomSort = new ProjectsDashboardNumberComparer(direction);
    }

    private sealed class ProjectsDashboardNumberComparer : System.Collections.IComparer
    {
        private readonly int _sign;

        public ProjectsDashboardNumberComparer(ListSortDirection direction)
        {
            _sign = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public int Compare(object? x, object? y)
        {
            var left = x as ProjectsDashboardRowVm;
            var right = y as ProjectsDashboardRowVm;
            if (left is null && right is null)
                return 0;
            if (left is null)
                return -_sign;
            if (right is null)
                return _sign;

            var byNumber = ProjectNumberSort.CompareKeys(left.ProjectNumberSortKey, right.ProjectNumberSortKey);
            if (byNumber != 0)
                return byNumber * _sign;

            return left.ProjectId.CompareTo(right.ProjectId) * _sign;
        }
    }
}
