using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Management dialog for Task-to-Project status mappings.
/// </summary>
public partial class StatusMappingWindow : Window
{
    public StatusMappingWindow()
    {
        InitializeComponent();

        var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var statusMappingService = App.ServiceProvider.GetRequiredService<SiNetSQL.Services.IStatusMappingService>();
        DataContext = new StatusMappingViewModel(dbContextFactory, statusMappingService);
    }
}
