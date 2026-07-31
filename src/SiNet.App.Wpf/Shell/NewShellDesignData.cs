using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Design-time sample data for <see cref="NewShellWindow"/> so the XAML designer can render the clean
/// shell (header, hierarchical menu, status bar) without DI or a database.
/// </summary>
public sealed class NewShellDesignData : NewShellViewModel
{
    public NewShellDesignData()
        : base(
            CreateSampleMenu(),
            currentUserDisplay: "דני ישראל",
            currentProjectContext: CreateDesignProjectContext(),
            openNewProject: static () => { })
    {
        StatusText = "מוכן — מצב עיצוב";
    }

    private static ICurrentProjectContext CreateDesignProjectContext()
    {
        var context = new InMemoryCurrentProjectContext();
        context.SetCurrentProjectAsync(new ProjectSummaryDto(
            ProjectId: 1,
            ProjectNumber: "1234",
            ProjectName: "מגדל השחר",
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true)).GetAwaiter().GetResult();
        return context;
    }

    private static IEnumerable<NewShellMenuItem> CreateSampleMenu() =>
    [
        NewShellMenuItem.Group("פרויקטים ותבניות",
        [
            new NewShellMenuItem("פתיחת פרויקט חדש", static () => { }),
            new NewShellMenuItem("ריכוז פרויקטים", static () => { }),
            new NewShellMenuItem("מיילים", static () => { }),
            new NewShellMenuItem("בעבודה 2", static () => { }),
        ]),
        NewShellMenuItem.Group("משימות",
        [
            new NewShellMenuItem("לוח משימות", static () => { }),
            new NewShellMenuItem("דוחות ביקורת", static () => { }),
        ]),
        NewShellMenuItem.Group("מנהלה",
        [
            new NewShellMenuItem("הגדרות אישיות", static () => { }),
            new NewShellMenuItem("מצב מערכת", static () => { }),
        ]),
    ];
}
