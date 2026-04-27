using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services.AccBootstrap;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Management window for UserGroups: create groups, add/remove members, set default assignee.
/// </summary>
public partial class UserGroupManagementWindow : Window
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private List<UserGroup> _groups = [];
    private List<Siuser> _allUsers = [];

    public UserGroupManagementWindow()
    {
        InitializeComponent();
        _dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        Loaded += async (_, _) => await LoadDataAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Data Loading
    // ════════════════════════════════════════════════════════════════════════

    private async Task LoadDataAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        _groups = await db.UserGroups
            .Include(g => g.Memberships)
                .ThenInclude(m => m.Siuser)
            .Include(g => g.DefaultAssignee)
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync();

        _allUsers = await db.Siusers
            .Where(u => u.IsActive)
            .OrderBy(u => u.Name)
            .ToListAsync();

        GroupListBox.ItemsSource = _groups;
        if (_groups.Count > 0)
            GroupListBox.SelectedIndex = 0;
    }

    private async void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Auto-save previous group details
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is UserGroup previousGroup)
            await SaveGroupDetailsAsync(previousGroup);

        if (GroupListBox.SelectedItem is not UserGroup group)
        {
            ClearDetails();
            return;
        }

        GroupCodeTextBox.Text = group.Code;
        GroupNameTextBox.Text = group.Name;
        GroupDescriptionTextBox.Text = group.Description ?? string.Empty;

        // Members
        var members = group.Memberships
            .Select(m => m.Siuser)
            .Where(u => u.IsActive)
            .OrderBy(u => u.Name)
            .ToList();

        MembersGrid.ItemsSource = members;

        // Default assignee combo — only show current members
        DefaultAssigneeCombo.ItemsSource = members;
        DefaultAssigneeCombo.SelectedValue = group.DefaultAssigneeId;
    }

    private void ClearDetails()
    {
        GroupCodeTextBox.Text = string.Empty;
        GroupNameTextBox.Text = string.Empty;
        GroupDescriptionTextBox.Text = string.Empty;
        MembersGrid.ItemsSource = null;
        DefaultAssigneeCombo.ItemsSource = null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Group CRUD
    // ════════════════════════════════════════════════════════════════════════

    private async void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "שם הקבוצה החדשה:", "קבוצה חדשה", "");

        if (string.IsNullOrWhiteSpace(name)) return;

        var code = Microsoft.VisualBasic.Interaction.InputBox(
            "קוד הקבוצה (אנגלית, ללא רווחים):", "קוד קבוצה", 
            name.Replace(" ", ""));

        if (string.IsNullOrWhiteSpace(code)) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        if (await db.UserGroups.AnyAsync(g => g.Code == code))
        {
            MessageBox.Show($"קבוצה עם קוד '{code}' כבר קיימת.", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        db.UserGroups.Add(new UserGroup
        {
            Code = code,
            Name = name,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        await LoadDataAsync();
        GroupListBox.SelectedItem = _groups.FirstOrDefault(g => g.Code == code);
    }

    private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not UserGroup group) return;

        var result = MessageBox.Show(
            $"למחוק את הקבוצה '{group.Name}'?\nהפעולה תסיר את כל חברי הקבוצה.",
            "מחיקת קבוצה", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var toDelete = await db.UserGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == group.Id);

        if (toDelete is null) return;

        // Soft delete: deactivate instead of hard delete
        toDelete.IsActive = false;
        await db.SaveChangesAsync();

        await LoadDataAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Members
    // ════════════════════════════════════════════════════════════════════════

    private async void AddMemberButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not UserGroup group) return;

        // Show user picker — exclude users already in group
        var existingIds = group.Memberships.Select(m => m.SiuserId).ToHashSet();
        var available = _allUsers.Where(u => !existingIds.Contains(u.Id)).ToList();

        if (available.Count == 0)
        {
            MessageBox.Show("כל המשתמשים הפעילים כבר חברים בקבוצה.", "אין משתמשים זמינים",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new UserPickerDialog(available) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedUser is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.UserGroupMemberships.Add(new UserGroupMembership
        {
            UserGroupId = group.Id,
            SiuserId = picker.SelectedUser.Id,
        });
        await db.SaveChangesAsync();

        await LoadDataAsync();
        GroupListBox.SelectedItem = _groups.FirstOrDefault(g => g.Id == group.Id);
    }

    private async void RemoveMemberButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not UserGroup group) return;
        if (MembersGrid.SelectedItem is not Siuser member) return;

        var result = MessageBox.Show(
            $"להסיר את {member.Name} מקבוצת '{group.Name}'?",
            "הסרת חבר", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var membership = await db.UserGroupMemberships
            .FirstOrDefaultAsync(m => m.UserGroupId == group.Id && m.SiuserId == member.Id);

        if (membership is null) return;

        db.UserGroupMemberships.Remove(membership);

        // If removed member was default assignee, clear it
        var grp = await db.UserGroups.FindAsync(group.Id);
        if (grp?.DefaultAssigneeId == member.Id)
            grp.DefaultAssigneeId = null;

        await db.SaveChangesAsync();

        await LoadDataAsync();
        GroupListBox.SelectedItem = _groups.FirstOrDefault(g => g.Id == group.Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Default Assignee
    // ════════════════════════════════════════════════════════════════════════

    private async void ClearDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not UserGroup group) return;

        DefaultAssigneeCombo.SelectionChanged -= DefaultAssigneeCombo_SelectionChanged;
        await SaveDefaultAssigneeAsync(group.Id, null);
        DefaultAssigneeCombo.SelectedValue = null;
        group.DefaultAssigneeId = null;
        DefaultAssigneeCombo.SelectionChanged += DefaultAssigneeCombo_SelectionChanged;
    }

    private async void DefaultAssigneeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not UserGroup group) return;
        if (DefaultAssigneeCombo.SelectedValue is not int userId) return;
        if (group.DefaultAssigneeId == userId) return;

        await SaveDefaultAssigneeAsync(group.Id, userId);
        group.DefaultAssigneeId = userId;
    }

    private async Task SaveDefaultAssigneeAsync(int groupId, int? userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var grp = await db.UserGroups.FindAsync(groupId);
        if (grp is null) return;

        grp.DefaultAssigneeId = userId;
        await db.SaveChangesAsync();
    }

    // Save group details + default assignee when selection changes away
    // (auto-save pattern matching ManagementSettingsWindow)
    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (GroupListBox.SelectedItem is UserGroup group)
            await SaveGroupDetailsAsync(group);
        base.OnClosing(e);
    }

    private async Task SaveGroupDetailsAsync(UserGroup group)
    {
        var name = GroupNameTextBox.Text?.Trim();
        var code = GroupCodeTextBox.Text?.Trim();
        var desc = GroupDescriptionTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code)) return;

        if (group.Name == name && group.Code == code
            && (group.Description ?? "") == (desc ?? ""))
            return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var grp = await db.UserGroups.FindAsync(group.Id);
        if (grp is null) return;

        grp.Code = code;
        grp.Name = name;
        grp.Description = string.IsNullOrWhiteSpace(desc) ? null : desc;
        await db.SaveChangesAsync();

        // Update in-memory object
        group.Code = code;
        group.Name = name;
        group.Description = desc;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Bulk: Reconcile All Projects (members + roles + folder permissions)
    // ════════════════════════════════════════════════════════════════════════

    private async void ReconcileAllProjectsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "פעולה זו תסנכרן את חברי הפרויקטים, התפקידים והרשאות התיקיות עבור כל פרויקטי ACC הקיימים.\n\nלהמשיך?",
            "עדכון כל הגדרות הפרויקטים",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IAccProjectProvisioningService? provisioning;
        try
        {
            provisioning = App.ServiceProvider.GetRequiredService<IAccProjectProvisioningService>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"שירות הסנכרון אינו זמין:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReconcileAllProjectsButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var summary = await provisioning.ReconcileAllProjectsAsync(CancellationToken.None);
            MessageBox.Show(this, $"עדכון הסתיים.\n\n{summary}",
                "סיום", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"העדכון נכשל:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            ReconcileAllProjectsButton.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Diagnostic: minimal folder-permissions probe (creates a throwaway project)
    // ════════════════════════════════════════════════════════════════════════

    private async void ProbeFolderPermissionsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "פעולה זו תיצור פרויקט בדיקה חדש ב-ACC (בשם SI-PermProbe-...), תקצה Project Admin, ותנסה להגדיר הרשאות Engineer על תיקיית השורש.\n\nלא יבוצעו פעולות נוספות (לא יווספו משתמשים, לא ייווצרו תיקיות נוספות, לא יישמר mapping).\n\nפרויקט הבדיקה יישאר ב-ACC ויהיה צריך להעבירו לארכיון/למחוק ידנית.\n\nלהמשיך?",
            "בדיקת הרשאות תיקייה",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IAccProjectProvisioningService? provisioning;
        try
        {
            provisioning = App.ServiceProvider.GetRequiredService<IAccProjectProvisioningService>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"שירות הסנכרון אינו זמין:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProbeFolderPermissionsButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var summary = await provisioning.ProbeFolderPermissionsAsync(CancellationToken.None);
            MessageBox.Show(this, $"בדיקה הסתיימה.\n\n{summary}",
                "סיום", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"הבדיקה נכשלה:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            ProbeFolderPermissionsButton.IsEnabled = true;
        }
    }

    private async void ProbeFromTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        // Ask the user for the exact template name (Hebrew names supported).
        var input = new TextInputDialog(
            "בדיקה עם תבנית",
            "הזן את שם התבנית ב-ACC (בדיוק כפי שמופיע במסך Account Admin > Templates):",
            defaultValue: "שיא חדש בע\"מ")
        {
            Owner = this
        };
        if (input.ShowDialog() != true)
            return;

        var templateName = (input.ResponseText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(templateName))
            return;

        var confirm = MessageBox.Show(
            this,
            $"פעולה זו תאתר את התבנית '{templateName}' בחשבון ACC ותיצור ממנה פרויקט בדיקה חדש (SI-TplProbe-...).\nלאחר מכן תנסה להגדיר הרשאות Engineer על תיקיית השורש.\n\nפרויקט הבדיקה יישאר ב-ACC.\n\nלהמשיך?",
            "בדיקה עם תבנית",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IAccProjectProvisioningService? provisioning;
        try
        {
            provisioning = App.ServiceProvider.GetRequiredService<IAccProjectProvisioningService>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"שירות הסנכרון אינו זמין:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProbeFromTemplateButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var summary = await provisioning.ProbeFolderPermissionsFromTemplateAsync(templateName, CancellationToken.None);
            MessageBox.Show(this, $"בדיקה הסתיימה.\n\n{summary}",
                "סיום", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"הבדיקה נכשלה:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            ProbeFromTemplateButton.IsEnabled = true;
        }
    }
}

/// <summary>
/// Simple dialog to pick a user from a list.
/// </summary>
public partial class UserPickerDialog : Window
{
    public Siuser? SelectedUser { get; private set; }

    public UserPickerDialog(List<Siuser> users)
    {
        Title = "בחירת משתמש";
        Width = 350;
        Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = (System.Windows.Media.Brush)FindResource("AppBackground");

        var panel = new DockPanel { Margin = new Thickness(10) };

        var searchBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(5, 3, 5, 3),
        };
        searchBox.SetValue(DockPanel.DockProperty, Dock.Top);

        var listBox = new ListBox
        {
            DisplayMemberPath = "Name",
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CCC")),
        };

        var okButton = new Button
        {
            Content = "בחר",
            Padding = new Thickness(15, 5, 15, 5),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        okButton.SetValue(DockPanel.DockProperty, Dock.Bottom);

        listBox.ItemsSource = users;

        searchBox.TextChanged += (_, _) =>
        {
            var filter = searchBox.Text?.Trim() ?? "";
            listBox.ItemsSource = string.IsNullOrEmpty(filter)
                ? users
                : users.Where(u => (u.Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        };

        okButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is Siuser user)
            {
                SelectedUser = user;
                DialogResult = true;
            }
        };

        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is Siuser user)
            {
                SelectedUser = user;
                DialogResult = true;
            }
        };

        panel.Children.Add(searchBox);
        panel.Children.Add(okButton);
        panel.Children.Add(listBox);
        Content = panel;
    }
}

/// <summary>
/// Minimal single-line text input dialog. Sets <see cref="ResponseText"/> when the user
/// confirms; <see cref="Window.DialogResult"/> reflects accept/cancel.
/// </summary>
public class TextInputDialog : Window
{
    public string? ResponseText { get; private set; }

    public TextInputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var textBox = new TextBox
        {
            Text = defaultValue,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(textBox);

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var okButton = new Button { Content = "אישור", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new Button { Content = "ביטול", Width = 80, IsCancel = true };
        okButton.Click += (_, _) =>
        {
            ResponseText = textBox.Text;
            DialogResult = true;
        };
        buttonsPanel.Children.Add(okButton);
        buttonsPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonsPanel);

        Content = panel;
        Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
    }
}
