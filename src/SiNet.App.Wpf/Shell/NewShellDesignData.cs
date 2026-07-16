using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Design-time sample data for <see cref="NewShellWindow"/> so the XAML designer can render the clean
/// shell (header, migrated-only menu, status bar) without DI or a database. Referenced via
/// <c>d:DataContext</c> only; never used at runtime.
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
        // Design-time labels mirror production pilot menu text (see NewShellFactory); Inspection harness is DEBUG-only at runtime.
        new NewShellMenuItem("דוא\"ל", static () => { }, "Inbox hosted in main shell"),
        new NewShellMenuItem("משימות — Task Workbench", static () => { }, "תורים אישיים"),
        new NewShellMenuItem("פתיחת פרויקט חדש", static () => { }, "יצירת פרויקט חדש"),
        new NewShellMenuItem("ביקורת (מעטפת — DEBUG)", static () => { }, "Developer harness — not in Release shell menu"),
        new NewShellMenuItem("הגדרות", static () => { }, "בקרוב", isAvailable: false),
    ];
}
