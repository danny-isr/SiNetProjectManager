using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Optional picker for a <see cref="TaskResultDefinition"/> when a task is being closed.
/// Allows business outcomes (e.g. <c>QuoteSent</c>, <c>AuthorityApproved</c>) to be recorded
/// alongside the generic status change so workflow rules can branch on the result.
/// <para>
/// The dialog is purely advisory — the user can skip it and the close will still proceed.
/// </para>
/// </summary>
public partial class TaskResultPickerDialog : Window
{
    private List<TaskResultDefinition> _allResults = [];

    /// <summary>The selected result, or null if the user skipped / cancelled.</summary>
    public TaskResultDefinition? SelectedResult { get; private set; }

    /// <summary>True when the user explicitly skipped the picker (closed without a result).</summary>
    public bool Skipped { get; private set; }

    public TaskResultPickerDialog(int? taskTypeId = null)
    {
        InitializeComponent();
        LoadResults(taskTypeId);
    }

    private void LoadResults(int? taskTypeId)
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var db = dbFactory.CreateDbContext();
            _allResults = db.TaskResultDefinitions
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.Category)
                .ThenBy(r => r.SortOrder)
                .ThenBy(r => r.Name)
                .ToList();

            var categories = _allResults
                .Select(r => r.Category ?? "כללי")
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            categories.Insert(0, "— הכל —");

            CategoryComboBox.ItemsSource = categories;
            CategoryComboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            HelpText.Text = $"שגיאה בטעינת תוצאות: {ex.Message}";
        }
    }

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedItem is not string cat) return;

        IEnumerable<TaskResultDefinition> filtered = _allResults;
        if (cat != "— הכל —")
        {
            filtered = _allResults.Where(r => (r.Category ?? "כללי") == cat);
        }

        ResultComboBox.ItemsSource = filtered.ToList();
        if (ResultComboBox.Items.Count > 0)
            ResultComboBox.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedResult = ResultComboBox.SelectedItem as TaskResultDefinition;
        Skipped = SelectedResult is null;
        DialogResult = true;
    }

    private void NoResult_Click(object sender, RoutedEventArgs e)
    {
        SelectedResult = null;
        Skipped = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
