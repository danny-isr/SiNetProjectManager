namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Design-time sample data for <see cref="NewShellWindow"/> so the XAML designer can render the clean
/// shell (header, migrated-only menu, status bar) without DI or a database. Referenced via
/// <c>d:DataContext</c> only; never used at runtime.
/// </summary>
public sealed class NewShellDesignData : NewShellViewModel
{
    public NewShellDesignData()
        : base(CreateSampleMenu(), currentUserDisplay: "דני ישראל", currentProjectDisplay: "מגדל השחר — 1234")
    {
        StatusText = "מוכן — מצב עיצוב";
    }

    private static IEnumerable<NewShellMenuItem> CreateSampleMenu() =>
    [
        new NewShellMenuItem("דוא\"ל (שכפול חזותי)", static () => { }, "פתיחת מסך הדוא\"ל החדש"),
        new NewShellMenuItem("ביקורת (מעטפת)", static () => { }, "פתיחת מעטפת הביקורת החדשה"),
        new NewShellMenuItem("בחירת פרויקט", static () => { }, "בדיקת הקשר הפרויקט"),
        new NewShellMenuItem("הגדרות", static () => { }, "בקרוב", isAvailable: false),
    ];
}
