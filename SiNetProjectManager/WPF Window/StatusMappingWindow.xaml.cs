using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;

namespace SiNetProjectManager.WPF_Window;

/// <summary>
/// Management dialog for Task-to-Project status mappings.
/// </summary>
public partial class StatusMappingWindow : Window
{
    public StatusMappingWindow()
    {
        InitializeComponent();

        var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        DataContext = new StatusMappingViewModel(dbContextFactory);
    }
}
